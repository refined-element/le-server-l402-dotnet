using System.Text.Json.Serialization;

namespace L402Server;

/// <summary>
/// Configuration for <see cref="L402ServerClient"/>.
/// </summary>
public sealed class L402ServerOptions
{
    /// <summary>
    /// Your Lightning Enable merchant API key. Required.
    /// Generate one at https://api.lightningenable.com/dashboard/settings.
    /// </summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// Base URL for the L402 producer API. Defaults to the hosted Lightning Enable
    /// instance. Override for testing against a local dev instance.
    /// </summary>
    public string BaseUrl { get; init; } = "https://api.lightningenable.com";

    /// <summary>
    /// Per-request timeout. Defaults to 10 seconds.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Request body for <see cref="L402ServerClient.CreateChallengeAsync"/>.
/// </summary>
public sealed record CreateChallengeRequest
{
    /// <summary>
    /// The resource the challenge is for, typically the request path. Bound as
    /// a caveat in the macaroon so the resulting token can only access this
    /// exact resource. Example: "/api/weather/forecast".
    /// </summary>
    [JsonPropertyName("resource")]
    public required string Resource { get; init; }

    /// <summary>
    /// Price in satoshis. Must be ≥ 1.
    /// </summary>
    [JsonPropertyName("priceSats")]
    public required long PriceSats { get; init; }

    /// <summary>
    /// Optional description embedded in the Lightning invoice.
    /// Visible to the payer in their wallet UI.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Optional idempotency key. If the same key is sent twice within the
    /// invoice's expiry window, the same challenge is returned (no duplicate
    /// invoice). Defaults to none — the server falls back to client IP for
    /// deduplication. Truncated to 256 chars server-side.
    /// </summary>
    [JsonIgnore]
    public string? IdempotencyKey { get; init; }
}

/// <summary>
/// The 402 challenge returned from <see cref="L402ServerClient.CreateChallengeAsync"/>.
/// </summary>
public sealed record Challenge
{
    /// <summary>
    /// BOLT11 Lightning invoice the client must pay.
    /// </summary>
    [JsonPropertyName("invoice")]
    public required string Invoice { get; init; }

    /// <summary>
    /// Base64-encoded macaroon containing the payment hash and caveats.
    /// </summary>
    [JsonPropertyName("macaroon")]
    public required string Macaroon { get; init; }

    /// <summary>
    /// Payment hash (hex) — links the macaroon to the invoice.
    /// </summary>
    [JsonPropertyName("paymentHash")]
    public required string PaymentHash { get; init; }

    /// <summary>
    /// When the Lightning invoice expires.
    /// </summary>
    [JsonPropertyName("expiresAt")]
    public required DateTime ExpiresAt { get; init; }

    /// <summary>
    /// The resource the challenge is bound to (echoed from the request).
    /// </summary>
    [JsonPropertyName("resource")]
    public required string Resource { get; init; }

    /// <summary>
    /// Price in satoshis (echoed from the request).
    /// </summary>
    [JsonPropertyName("priceSats")]
    public required long PriceSats { get; init; }

    /// <summary>
    /// MPP-formatted WWW-Authenticate challenge header value, if MPP is
    /// enabled on the producer API.
    /// </summary>
    [JsonPropertyName("mppChallenge")]
    public string? MppChallenge { get; init; }
}

/// <summary>
/// Request body for <see cref="L402ServerClient.VerifyTokenAsync"/>.
/// </summary>
public sealed record VerifyTokenRequest
{
    /// <summary>
    /// Base64-encoded macaroon from the L402 credential
    /// (<c>Authorization: L402 macaroon:preimage</c>). Omit only for
    /// MPP-style preimage-only verification.
    /// </summary>
    [JsonPropertyName("macaroon")]
    public string? Macaroon { get; init; }

    /// <summary>
    /// Hex-encoded payment preimage (64 chars).
    /// </summary>
    [JsonPropertyName("preimage")]
    public required string Preimage { get; init; }
}

/// <summary>
/// Result from <see cref="L402ServerClient.VerifyTokenAsync"/>. The producer API
/// returns 200 OK for both valid and invalid tokens — inspect <see cref="Valid"/>.
/// </summary>
public sealed record VerificationResult
{
    /// <summary>
    /// Whether the token is valid. When false, <see cref="Error"/> is populated.
    /// </summary>
    [JsonPropertyName("valid")]
    public required bool Valid { get; init; }

    /// <summary>
    /// Human-readable failure reason. Only populated when <see cref="Valid"/> is false.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>
    /// The resource the token was bound to (from the macaroon's path caveat).
    /// </summary>
    [JsonPropertyName("resource")]
    public string? Resource { get; init; }

    /// <summary>
    /// Merchant ID the token was bound to.
    /// </summary>
    [JsonPropertyName("merchantId")]
    public int? MerchantId { get; init; }

    /// <summary>
    /// Amount the token was issued for, in satoshis.
    /// </summary>
    [JsonPropertyName("amountSats")]
    public long? AmountSats { get; init; }

    /// <summary>
    /// Payment hash from the macaroon identifier.
    /// </summary>
    [JsonPropertyName("paymentHash")]
    public string? PaymentHash { get; init; }
}
