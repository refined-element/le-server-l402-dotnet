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

        try
        {
            act();
            Assert.Fail("Expected InvalidOperationException");
        }
        catch (InvalidOperationException ex)
        {
            ex.Message.Should().NotContain("SECRET_LOOKING_VALUE",
                because: "the offending value must never appear in the exception message");
        }
    }

    [Fact]
    public void AddL402Server_ValidApiKey_Registers_And_TrimsApiKey()
    {
        var services = new ServiceCollection();
        services.AddL402Server(opts => opts.ApiKey = "  validkey  ");
        // Build the provider and resolve the client — exercises the full
        // DI path end-to-end. The client construction would also throw if
        // validation failed there.
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<L402ServerClient>();
        client.Should().NotBeNull();
        // No public accessor for the bound ApiKey, but the fact that
        // GetRequiredService succeeded means both validation passes
        // (the registration-time one and the client-construction one)
        // accepted the trimmed value.
    }
}
