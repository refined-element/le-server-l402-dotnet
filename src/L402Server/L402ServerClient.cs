using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace L402Server;

/// <summary>
/// Server-side client for Lightning Enable's L402 producer API. Wraps two
/// endpoints:
///
/// <list type="bullet">
///   <item><see cref="CreateChallengeAsync"/> → <c>POST /api/l402/challenges</c> —
///     mint a Lightning invoice + macaroon for a given resource and price.</item>
///   <item><see cref="VerifyTokenAsync"/> → <c>POST /api/l402/challenges/verify</c> —
///     validate an incoming L402 token (macaroon + preimage).</item>
/// </list>
///
/// <para>
/// <b>No protocol logic lives in this SDK.</b> Lightning Enable signs
/// macaroons, mints invoices, verifies preimages, and tracks consumed tokens
/// for replay protection. This client is purely an HTTP wrapper.
/// </para>
///
/// <example>
/// Basic usage:
/// <code>
/// var client = new L402ServerClient(new L402ServerOptions
/// {
///     ApiKey = Environment.GetEnvironmentVariable("LIGHTNING_ENABLE_API_KEY")!,
/// });
///
/// var challenge = await client.CreateChallengeAsync(new CreateChallengeRequest
/// {
///     Resource = "/api/premium/weather",
///     PriceSats = 100,
///     Description = "Premium weather forecast",
/// });
///
/// // ... send 402 Payment Required to caller, wait for retry with Authorization: L402 mac:preimage ...
///
/// var result = await client.VerifyTokenAsync(new VerifyTokenRequest
/// {
///     Macaroon = parsedMacaroon,
///     Preimage = parsedPreimage,
/// });
/// if (result.Valid) { /* serve the response */ }
/// </code>
/// </example>
/// </summary>
public sealed class L402ServerClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _baseUrl;

    /// <summary>
    /// Constructs a client that owns its <see cref="HttpClient"/>. Suitable for
    /// short-lived usage. For long-lived applications, prefer the overload that
    /// accepts an injected <see cref="HttpClient"/> from <c>IHttpClientFactory</c>.
    /// </summary>
    public L402ServerClient(L402ServerOptions options)
        : this(options, http: null) { }

    /// <summary>
    /// Constructs a client using an externally-managed <see cref="HttpClient"/>
    /// (typically from <c>IHttpClientFactory</c>). The client will NOT dispose
    /// the injected HttpClient.
    /// </summary>
    public L402ServerClient(L402ServerOptions options, HttpClient? http)
    {
        ArgumentNullException.ThrowIfNull(options);
        // Validation extracted to ApiKeyValidator so the rules match
        // exactly between client construction and DI registration.
        // Exception parameter name is the field that's actually wrong
        // ("ApiKey"), not the enclosing options object — makes the
        // resulting ArgumentException point at the real problem.
        // The returned trimmed value is then used for the outbound
        // X-API-Key header so a stray leading/trailing space in
        // configuration doesn't get sent on the wire.
        var trimmedApiKey = ApiKeyValidator.ValidateAndTrim(
            options.ApiKey,
            msg => new ArgumentException(msg, paramName: "ApiKey"));

        _baseUrl = options.BaseUrl.TrimEnd('/');

        if (http is null)
        {
            _http = new HttpClient { Timeout = options.Timeout };
            _ownsHttp = true;
        }
        else
        {
            _http = http;
            _ownsHttp = false;
            // Honor the caller's timeout unless the injected HttpClient already
            // has a more restrictive one. We don't override an explicit caller
            // setting.
            if (http.Timeout == TimeSpan.FromSeconds(100)) // default
            {
                http.Timeout = options.Timeout;
            }
        }

        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Add("X-API-Key", trimmedApiKey);
    }

    /// <summary>
    /// Mint an L402 challenge for the given resource. Send the returned
    /// challenge back to the caller as part of a 402 Payment Required
    /// response.
    /// </summary>
    /// <exception cref="ArgumentException">When request fields are invalid (empty resource, priceSats &lt; 1, empty preimage on verify).</exception>
    /// <exception cref="L402AuthException">On 401.</exception>
    /// <exception cref="L402PlanException">On 403 (L402 not enabled on plan).</exception>
    /// <exception cref="L402ApiException">On other non-2xx.</exception>
    /// <exception cref="L402NetworkException">On timeout or transport failure.</exception>
    public async Task<Challenge> CreateChallengeAsync(
        CreateChallengeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Resource))
        {
            throw new ArgumentException("Resource is required.", nameof(request));
        }
        if (request.PriceSats < 1)
        {
            throw new ArgumentException("PriceSats must be ≥ 1.", nameof(request));
        }

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post, $"{_baseUrl}/api/l402/challenges")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            httpRequest.Headers.Add("X-Idempotency-Key", request.IdempotencyKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new L402NetworkException(
                $"Request to {_baseUrl}/api/l402/challenges timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new L402NetworkException(
                $"Network error talking to {_baseUrl}/api/l402/challenges: {ex.Message}", ex);
        }

        try
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var challenge = await response.Content.ReadFromJsonAsync<Challenge>(
                    JsonOptions, cancellationToken).ConfigureAwait(false);
                return challenge ?? throw new L402ApiException(
                    200, "Empty response body from L402 producer API.");
            }

            await ThrowForStatusAsync(response, cancellationToken).ConfigureAwait(false);
            // ThrowForStatusAsync always throws on non-2xx.
            throw new L402ApiException(
                (int)response.StatusCode,
                "Unexpected response from L402 producer API.");
        }
        finally
        {
            response.Dispose();
        }
    }

    /// <summary>
    /// Verify an L402 token. The producer API returns 200 OK for both valid
    /// and invalid tokens — inspect <see cref="VerificationResult.Valid"/>
    /// rather than relying on HTTP status. Non-200 responses indicate a
    /// higher-level problem (auth, plan, transport).
    /// </summary>
    public async Task<VerificationResult> VerifyTokenAsync(
        VerifyTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Preimage))
        {
            throw new ArgumentException("Preimage is required.", nameof(request));
        }

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post, $"{_baseUrl}/api/l402/challenges/verify")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new L402NetworkException(
                $"Request to {_baseUrl}/api/l402/challenges/verify timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new L402NetworkException(
                $"Network error talking to {_baseUrl}/api/l402/challenges/verify: {ex.Message}", ex);
        }

        try
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var result = await response.Content.ReadFromJsonAsync<VerificationResult>(
                    JsonOptions, cancellationToken).ConfigureAwait(false);
                return result ?? throw new L402ApiException(
                    200, "Empty response body from L402 producer API.");
            }

            await ThrowForStatusAsync(response, cancellationToken).ConfigureAwait(false);
            throw new L402ApiException(
                (int)response.StatusCode,
                "Unexpected response from L402 producer API.");
        }
        finally
        {
            response.Dispose();
        }
    }

    private static async Task ThrowForStatusAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Ignore — fall through with body = null.
        }

        var (errorMessage, currentPlan) = TryParseError(body);

        switch ((int)response.StatusCode)
        {
            case 401:
                throw new L402AuthException(errorMessage ?? "Merchant API key is missing or invalid.");
            case 403:
                throw new L402PlanException(
                    errorMessage ?? "L402 is not enabled on this merchant's plan.",
                    currentPlan);
            default:
                throw new L402ApiException(
                    (int)response.StatusCode,
                    errorMessage ?? $"HTTP {(int)response.StatusCode} from {response.RequestMessage?.RequestUri}",
                    body);
        }
    }

    private static (string? Message, string? CurrentPlan) TryParseError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(body);
            string? msg = null;
            string? plan = null;
            if (doc.RootElement.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String)
                msg = errEl.GetString();
            else if (doc.RootElement.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
                msg = msgEl.GetString();
            if (doc.RootElement.TryGetProperty("current_plan", out var planEl) && planEl.ValueKind == JsonValueKind.String)
                plan = planEl.GetString();
            return (msg, plan);
        }
        catch
        {
            return (null, null);
        }
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
