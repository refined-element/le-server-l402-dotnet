using System.Text.RegularExpressions;

namespace L402Server;

/// <summary>
/// Centralized validation + trimming for the merchant ApiKey.
/// Used both at client construction (L402ServerClient) and at DI
/// registration (ServiceCollectionExtensions) so the rules can't
/// drift between the two entry points.
/// </summary>
/// <remarks>
/// Exception messages NEVER include the offending value. Even
/// placeholder strings like <c>${LIGHTNING_ENABLE_API_KEY}</c>
/// aren't echoed back, because:
/// <list type="bullet">
///   <item>InvalidOperationException at startup is commonly captured
///   by structured loggers and shipped to log aggregators.</item>
///   <item>A user could accidentally pass a partial real key that
///   superficially matches the placeholder pattern.</item>
///   <item>Defense in depth — the diagnostic message names the
///   most common causes; the actual problem string isn't needed
///   for the user to act on it.</item>
/// </list>
/// </remarks>
internal static class ApiKeyValidator
{
    private static readonly Regex PlaceholderPattern =
        new(@"^\$\{[^}]+\}$", RegexOptions.Compiled);

    public const string MissingMessage =
        "L402ServerOptions.ApiKey is required. " +
        "Get one from your Lightning Enable dashboard at " +
        "https://api.lightningenable.com/dashboard/settings.";

    public const string PlaceholderMessage =
        "L402ServerOptions.ApiKey looks like an unresolved environment-variable " +
        "placeholder. This usually means a parent shell exported the literal " +
        "string \"${VAR_NAME}\" instead of the substituted value. " +
        "Common sources: launchSettings.json with unrendered ${env:NAME}, a " +
        "Dockerfile ENV line, or an unresolved IConfiguration value. Fix by " +
        "setting LIGHTNING_ENABLE_API_KEY to the real key or by clearing the " +
        "placeholder so configuration reads the correct value.";

    /// <summary>
    /// Validates the given ApiKey and returns its trimmed form. Throws
    /// the exception produced by <paramref name="throwFactory"/> on
    /// validation failure.
    /// </summary>
    /// <param name="apiKey">The raw ApiKey value (may have whitespace).</param>
    /// <param name="throwFactory">
    /// Builds the exception type the caller wants — typically
    /// <see cref="ArgumentException"/> at client construction or
    /// <see cref="InvalidOperationException"/> at DI registration.
    /// Receives a problem-description string; MUST NOT receive the
    /// offending ApiKey value.
    /// </param>
    /// <returns>The trimmed ApiKey, safe to use for outbound headers.</returns>
    public static string ValidateAndTrim(
        string? apiKey,
        Func<string, Exception> throwFactory)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw throwFactory(MissingMessage);
        }
        var trimmed = apiKey.Trim();
        if (PlaceholderPattern.IsMatch(trimmed))
        {
            throw throwFactory(PlaceholderMessage);
        }
        return trimmed;
    }
}
