using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace L402Server.Tests;

public class L402ServerClientTests
{
    private const string ApiKey = "test-merchant-key";
    private const string BaseUrl = "https://api.example";

    private static L402ServerClient MakeClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler);
        return new L402ServerClient(
            new L402ServerOptions { ApiKey = ApiKey, BaseUrl = BaseUrl },
            http);
    }

    // ------ Constructor validation ------

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Action act = () => new L402ServerClient(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_EmptyApiKey_Throws()
    {
        Action act = () => new L402ServerClient(new L402ServerOptions { ApiKey = "" });
        act.Should().Throw<ArgumentException>().WithMessage("*ApiKey*");
    }

    [Fact]
    public async Task Constructor_BaseUrlTrailingSlashIsStripped()
    {
        var handler = new StubHttpHandler(StubHttpHandler.Json(
            HttpStatusCode.OK,
            """{"invoice":"i","macaroon":"m","paymentHash":"p","expiresAt":"2026-05-12T00:00:00Z","resource":"/x","priceSats":1}"""));
        var http = new HttpClient(handler);
        using var client = new L402ServerClient(
            new L402ServerOptions { ApiKey = ApiKey, BaseUrl = $"{BaseUrl}///" },
            http);

        await client.CreateChallengeAsync(new CreateChallengeRequest { Resource = "/x", PriceSats = 1 });

        handler.LastRequest!.RequestUri!.ToString()
            .Should().Be($"{BaseUrl}/api/l402/challenges");
    }

    // ------ CreateChallengeAsync ------

    [Fact]
    public async Task CreateChallengeAsync_PostsExpectedRequestAndReturnsChallenge()
    {
        var handler = new StubHttpHandler(StubHttpHandler.Json(
            HttpStatusCode.OK,
            """
            {
              "invoice": "lnbc100n1...",
              "macaroon": "AgELbWFjYXJvb24=",
              "paymentHash": "abc123",
              "expiresAt": "2026-05-12T01:00:00Z",
              "resource": "/api/weather",
              "priceSats": 100,
              "mppChallenge": null
            }
            """));
        using var client = MakeClient(handler);

        var result = await client.CreateChallengeAsync(new CreateChallengeRequest
        {
            Resource = "/api/weather",
            PriceSats = 100,
            Description = "Weather forecast",
        });

        result.Invoice.Should().Be("lnbc100n1...");
        result.Macaroon.Should().Be("AgELbWFjYXJvb24=");
        result.PaymentHash.Should().Be("abc123");
        result.Resource.Should().Be("/api/weather");
        result.PriceSats.Should().Be(100);
        result.MppChallenge.Should().BeNull();

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().Should().Be($"{BaseUrl}/api/l402/challenges");
        handler.LastRequest.Headers.GetValues("X-API-Key").Should().Contain(ApiKey);

        var sentBody = JsonDocument.Parse(handler.LastRequestBody!);
        sentBody.RootElement.GetProperty("resource").GetString().Should().Be("/api/weather");
        sentBody.RootElement.GetProperty("priceSats").GetInt64().Should().Be(100);
        sentBody.RootElement.GetProperty("description").GetString().Should().Be("Weather forecast");
    }

    [Fact]
    public async Task CreateChallengeAsync_WithIdempotencyKey_SendsHeaderNotBody()
    {
        var handler = new StubHttpHandler(StubHttpHandler.Json(
            HttpStatusCode.OK,
            """{"invoice":"i","macaroon":"m","paymentHash":"p","expiresAt":"2026-05-12T00:00:00Z","resource":"/x","priceSats":1}"""));
        using var client = MakeClient(handler);

        await client.CreateChallengeAsync(new CreateChallengeRequest
        {
            Resource = "/x",
            PriceSats = 1,
            IdempotencyKey = "req-abc-123",
        });

        handler.LastRequest!.Headers.GetValues("X-Idempotency-Key").Should().Contain("req-abc-123");
        // Body should NOT include the idempotency key
        handler.LastRequestBody.Should().NotContain("req-abc-123");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateChallengeAsync_EmptyResource_Throws(string resource)
    {
        using var client = MakeClient(new StubHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)));
        Func<Task> act = () => client.CreateChallengeAsync(new CreateChallengeRequest
        {
            Resource = resource,
            PriceSats = 1,
        });
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Resource*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateChallengeAsync_NonPositivePriceSats_Throws(long priceSats)
    {
        using var client = MakeClient(new StubHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)));
        Func<Task> act = () => client.CreateChallengeAsync(new CreateChallengeRequest
        {
            Resource = "/x",
            PriceSats = priceSats,
        });
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*PriceSats*");
    }

    [Fact]
    public async Task CreateChallengeAsync_Returns401_ThrowsL402AuthException()
    {
        var handler = new StubHttpHandler(StubHttpHandler.Json(
            HttpStatusCode.Unauthorized,
            """{"error":"Bad key"}"""));
        using var client = MakeClient(handler);
        Func<Task> act = () => client.CreateChallengeAsync(new CreateChallengeRequest
        {
            Resource = "/x",
            PriceSats = 1,
        });
        await act.Should().ThrowAsync<L402AuthException>();
    }

    [Fact]
    public async Task CreateChallengeAsync_Returns403_ThrowsL402PlanExceptionWithCurrentPlan()
    {
        var handler = new StubHttpHandler(StubHttpHandler.Json(
            HttpStatusCode.Forbidden,
            """
            {
              "error": "L402 not enabled",
              "current_plan": "starter",
              "action_required": "upgrade_plan"
            }
            """));
        using var client = MakeClient(handler);
        Func<Task> act = () => client.CreateChallengeAsync(new CreateChallengeRequest
        {
            Resource = "/x",
            PriceSats = 1,
        });
        var ex = (await act.Should().ThrowAsync<L402PlanException>()).Which;
        ex.CurrentPlan.Should().Be("starter");
    }

    [Fact]
    public async Task CreateChallengeAsync_Returns500_ThrowsL402ApiExceptionWithStatus()
    {
        var handler = new StubHttpHandler(StubHttpHandler.Json(
            HttpStatusCode.InternalServerError,
            """{"error":"Wallet down"}"""));
        using var client = MakeClient(handler);
        Func<Task> act = () => client.CreateChallengeAsync(new CreateChallengeRequest
        {
            Resource = "/x",
            PriceSats = 1,
        });
        var ex = (await act.Should().ThrowAsync<L402ApiException>()).Which;
        ex.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task CreateChallengeAsync_TransportFailure_ThrowsL402NetworkException()
    {
        var handler = new StubHttpHandler(_ =>
            throw new HttpRequestException("ECONNREFUSED"));
        using var client = MakeClient(handler);
        Func<Task> act = () => client.CreateChallengeAsync(new CreateChallengeRequest
        {
            Resource = "/x",
            PriceSats = 1,
        });
        await act.Should().ThrowAsync<L402NetworkException>();
    }

    // ------ VerifyTokenAsync ------

    [Fact]
    public async Task VerifyTokenAsync_ReturnsValidResult_OnValidToken()
    {
        var handler = new StubHttpHandler(StubHttpHandler.Json(
            HttpStatusCode.OK,
            """
            {
              "valid": true,
              "resource": "/api/weather",
              "merchantId": 42,
              "amountSats": 100,
              "paymentHash": "abc123",
              "error": null
            }
            """));
        using var client = MakeClient(handler);

        var result = await client.VerifyTokenAsync(new VerifyTokenRequest
        {
            Macaroon = "AgEL...",
            Preimage = "deadbeef",
        });

        result.Valid.Should().BeTrue();
        result.Resource.Should().Be("/api/weather");
        result.MerchantId.Should().Be(42);
        result.AmountSats.Should().Be(100);
        result.PaymentHash.Should().Be("abc123");
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task VerifyTokenAsync_ReturnsInvalidResult_WhenServerReturnsValidFalse()
    {
        var handler = new StubHttpHandler(StubHttpHandler.Json(
            HttpStatusCode.OK,
            """{"valid":false,"error":"Invalid preimage"}"""));
        using var client = MakeClient(handler);

        var result = await client.VerifyTokenAsync(new VerifyTokenRequest
        {
            Macaroon = "AgEL...",
            Preimage = "bad",
        });

        result.Valid.Should().BeFalse();
        result.Error.Should().Be("Invalid preimage");
        result.Resource.Should().BeNull();
    }

    [Fact]
    public async Task VerifyTokenAsync_OmitsMacaroon_ForMppOnlyVerification()
    {
        var handler = new StubHttpHandler(StubHttpHandler.Json(
            HttpStatusCode.OK,
            """{"valid":true,"paymentHash":"abc"}"""));
        using var client = MakeClient(handler);

        await client.VerifyTokenAsync(new VerifyTokenRequest { Preimage = "deadbeef" });

        var sentBody = JsonDocument.Parse(handler.LastRequestBody!);
        sentBody.RootElement.TryGetProperty("macaroon", out _).Should().BeFalse(
            "macaroon=null should be omitted from the wire format via JsonIgnoreCondition.WhenWritingNull");
        sentBody.RootElement.GetProperty("preimage").GetString().Should().Be("deadbeef");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task VerifyTokenAsync_EmptyPreimage_Throws(string preimage)
    {
        using var client = MakeClient(new StubHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)));
        Func<Task> act = () => client.VerifyTokenAsync(new VerifyTokenRequest
        {
            Macaroon = "x",
            Preimage = preimage,
        });
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Preimage*");
    }

    [Fact]
    public async Task VerifyTokenAsync_Returns401_ThrowsL402AuthException()
    {
        var handler = new StubHttpHandler(StubHttpHandler.Json(
            HttpStatusCode.Unauthorized,
            """{"error":"Bad key"}"""));
        using var client = MakeClient(handler);
        Func<Task> act = () => client.VerifyTokenAsync(new VerifyTokenRequest
        {
            Macaroon = "x",
            Preimage = "y",
        });
        await act.Should().ThrowAsync<L402AuthException>();
    }
}
