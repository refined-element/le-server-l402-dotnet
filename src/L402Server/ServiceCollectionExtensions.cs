using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace L402Server;

/// <summary>
/// DI helpers for registering <see cref="L402ServerClient"/> with the standard
/// .NET dependency injection container. Wires up an
/// <see cref="HttpClient"/> via <c>IHttpClientFactory</c> for proper
/// connection pooling.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="L402ServerClient"/> as a singleton, backed by a
    /// named <see cref="HttpClient"/> from <c>IHttpClientFactory</c>. Call this
    /// from your <c>Program.cs</c> / <c>Startup.cs</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddL402Server(opts =>
    /// {
    ///     opts.ApiKey = builder.Configuration["LightningEnable:ApiKey"]!;
    /// });
    /// </code>
    /// Then inject <see cref="L402ServerClient"/> into your services / controllers.
    /// </example>
    public static IServiceCollection AddL402Server(
        this IServiceCollection services,
        Action<L402ServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        // Bind options once at registration so the singleton sees them.
        var options = new L402ServerOptions();
        configure(options);
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException(
                "L402ServerOptions.ApiKey is required. Set opts.ApiKey inside the " +
                "AddL402Server(...) callback. Generate a key at " +
                "https://api.lightningenable.com/dashboard/settings.");
        }

        services.AddHttpClient("L402Server", http =>
        {
            http.Timeout = options.Timeout;
        });

        services.TryAddSingleton<L402ServerClient>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new L402ServerClient(options, factory.CreateClient("L402Server"));
        });

        return services;
    }
}
