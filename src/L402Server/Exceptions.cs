namespace L402Server;

/// <summary>
/// Base exception for all SDK-thrown errors.
/// </summary>
public class L402ServerException : Exception
{
    public L402ServerException(string message) : base(message) { }
    public L402ServerException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown on 401 Unauthorized — the merchant API key is missing, malformed,
/// expired, or revoked.
/// </summary>
public sealed class L402AuthException : L402ServerException
{
    public L402AuthException(string message = "Merchant API key is missing or invalid.")
        : base(message) { }
}

/// <summary>
/// Thrown on 403 Forbidden — the merchant exists and the key is valid, but
/// L402 is not enabled on their plan.
/// </summary>
public sealed class L402PlanException : L402ServerException
{
    /// <summary>
    /// The plan tier currently on the merchant (e.g. "starter"), if the
    /// server included it in the error payload.
    /// </summary>
    public string? CurrentPlan { get; }

    public L402PlanException(
        string message = "L402 is not enabled on this merchant's plan.",
        string? currentPlan = null) : base(message)
    {
        CurrentPlan = currentPlan;
    }
}

/// <summary>
/// Thrown for transport-level failures: timeout, DNS error, TLS error,
/// unreachable host. The <see cref="Exception.InnerException"/> carries the
/// original error.
/// </summary>
public sealed class L402NetworkException : L402ServerException
{
    public L402NetworkException(string message, Exception inner) : base(message, inner) { }
    public L402NetworkException(string message) : base(message) { }
}

/// <summary>
/// Thrown when the server returns a non-success status that doesn't map to a
/// more specific exception (400 validation, 429 rate-limit, 5xx, etc.).
/// </summary>
public sealed class L402ApiException : L402ServerException
{
    /// <summary>HTTP status code from the producer API.</summary>
    public int StatusCode { get; }

    /// <summary>Raw response body. May be a parsed object or a string.</summary>
    public string? ResponseBody { get; }

    public L402ApiException(int statusCode, string message, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}
