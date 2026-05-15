using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace L402Server.Tests;

/// <summary>
/// Tests for the DI registration path. Mirrors the placeholder +
/// trim behavior verified for direct L402ServerClient construction
/// over in <see cref="L402ServerClientTests"/>, so a future refactor
/// can't drift between the two entry points.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddL402Server_EmptyApiKey_Throws()
    {
        var services = new ServiceCollection();
        Action act = () => services.AddL402Server(opts => opts.ApiKey = "");
        act.Should().Throw<InvalidOperationException>().WithMessage("*ApiKey is required*");
    }

    [Fact]
    public void AddL402Server_WhitespaceApiKey_Throws()
    {
        var services = new ServiceCollection();
        Action act = () => services.AddL402Server(opts => opts.ApiKey = "   ");
        act.Should().Throw<InvalidOperationException>().WithMessage("*ApiKey is required*");
    }

    [Theory]
    [InlineData("${LIGHTNING_ENABLE_API_KEY}")]
    [InlineData("${SOME_OTHER_VAR}")]
    [InlineData("  ${PADDED_VAR}  ")]
    public void AddL402Server_PlaceholderApiKey_Throws(string placeholder)
    {
        var services = new ServiceCollection();
        Action act = () => services.AddL402Server(opts => opts.ApiKey = placeholder);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unresolved environment-variable placeholder*");
    }

    [Fact]
    public void AddL402Server_PlaceholderExceptionDoesNotEchoApiKeyValue()
    {
        // Even though the placeholder itself isn't a real secret, the
        // policy is "exception messages never contain anything that
        // could be credential-like content." A real key that
        // superficially matched the placeholder pattern (or any
        // future shape change) would otherwise leak into startup logs.
        var services = new ServiceCollection();
        Action act = () => services.AddL402Server(opts =>
            opts.ApiKey = "${SECRET_LOOKING_VALUE}");

        act.Should()
           .Throw<InvalidOperationException>()
           .Which.Message.Should().NotContain("SECRET_LOOKING_VALUE",
               because: "the offending value must never appear in the exception message");
    }

    [Fact]
    public async Task AddL402Server_ValidApiKey_Registers_And_SendsTrimmedKey()
    {
        // Proves trimming is actually applied to the outbound header,
        // not just that DI registration succeeded. The earlier version
        // of this test only resolved the client without observing the
        // value it actually uses — which would have happily passed even
        // if trim semantics regressed.
        //
        // Approach: register the service, override the named HttpClient
        // ("L402Server") with a stub handler, resolve the client, make
        // a real CreateChallenge call, and inspect the X-API-Key header
        // that went out. If trimming worked, the header has no spaces.
        var services = new ServiceCollection();
        services.AddL402Server(opts =>
        {
            opts.ApiKey = "  validkey  ";
            opts.BaseUrl = "https://api.example";
        });

        // Replace the HttpClient backing the named "L402Server" client
        // with one that uses our stub handler. The DI registration uses
        // IHttpClientFactory.CreateClient("L402Server"), so we configure
        // that named client's handler explicitly.
        var stub = new StubHttpHandler(StubHttpHandler.Json(
            System.Net.HttpStatusCode.OK,
            """{"invoice":"i","macaroon":"m","paymentHash":"p","expiresAt":"2026-05-12T00:00:00Z","resource":"/x","priceSats":1}"""));
        services.AddHttpClient("L402Server").ConfigurePrimaryHttpMessageHandler(() => stub);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<L402ServerClient>();
        await client.CreateChallengeAsync(new CreateChallengeRequest { Resource = "/x", PriceSats = 1 });

        stub.LastRequest.Should().NotBeNull();
        var apiKeyHeader = stub.LastRequest!.Headers.GetValues("X-API-Key").Single();
        apiKeyHeader.Should().Be("validkey",
            because: "the configured ApiKey had whitespace padding; the SDK must trim it before sending the header");
    }
}
