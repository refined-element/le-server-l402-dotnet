# L402Server

[![Discord](https://img.shields.io/discord/1405389254892195951?label=community&logo=discord&color=5865F2)](https://discord.gg/rX7NxHY8vx)


[![NuGet](https://img.shields.io/nuget/v/L402Server.svg)](https://www.nuget.org/packages/L402Server)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**L402 server SDK for .NET.** Mint Lightning invoices and macaroons. Verify L402 tokens. Wrap any HTTP API with pay-per-request Lightning payments — one `dotnet add package`, two methods.

This is the **producer-side** companion to [`L402Requests`](https://www.nuget.org/packages/L402Requests) (the consumer-side auto-paying HTTP client). Use `L402Requests` to *call* paid APIs from agents. Use `L402Server` to *build* paid APIs that those agents pay for.

## What you're paying for

`L402Server` is a thin .NET wrapper around [Lightning Enable](https://lightningenable.com)'s hosted producer API. The protocol-heavy work — invoice minting, macaroon signing, preimage verification, replay protection, wallet integration (Strike / OpenNode / LND / NWC) — all runs on Lightning Enable's side. The SDK is HTTP-client glue.

**Requires a Lightning Enable merchant API key** and an Agentic Commerce subscription ($99/mo Individual or $299/mo Business). Get both at [lightningenable.com/dashboard](https://api.lightningenable.com/dashboard).

## Install

```bash
dotnet add package L402Server
```

Target: .NET 8.0+.

## Quick start

```csharp
using L402Server;

var client = new L402ServerClient(new L402ServerOptions
{
    ApiKey = Environment.GetEnvironmentVariable("LIGHTNING_ENABLE_API_KEY")!,
});

// 1. On an unauthenticated incoming request, mint a challenge:
var challenge = await client.CreateChallengeAsync(new CreateChallengeRequest
{
    Resource = "/api/premium/weather",
    PriceSats = 100,
    Description = "Premium weather forecast",
});

// Send back as 402 Payment Required with the macaroon + invoice in headers.

// 2. When the client retries with Authorization: L402 <macaroon>:<preimage>,
//    parse the header and verify:
var result = await client.VerifyTokenAsync(new VerifyTokenRequest
{
    Macaroon = parsedMacaroon,
    Preimage = parsedPreimage,
});

if (result.Valid)
{
    // result.Resource → which path the token is bound to
    // result.AmountSats → how much was paid
    // Serve your real response.
}
```

## Dependency injection

For ASP.NET Core / generic-host apps, register the client via `IHttpClientFactory`:

```csharp
builder.Services.AddL402Server(opts =>
{
    opts.ApiKey = builder.Configuration["LightningEnable:ApiKey"]!;
});
```

Then inject `L402ServerClient` into your controllers / services. (For drop-in ASP.NET Core middleware that handles 402 issuance + verification automatically, see the forthcoming `L402Server.AspNetCore` package.)

## Surface

### `L402ServerOptions`

| Property | Type | Default | Notes |
|---|---|---|---|
| `ApiKey` | `string` | **required** | Lightning Enable merchant API key |
| `BaseUrl` | `string` | `https://api.lightningenable.com` | Override for testing |
| `Timeout` | `TimeSpan` | 10 seconds | Per-request timeout |

### `CreateChallengeAsync` → `Task<Challenge>`

Request fields: `Resource` (required, bound as macaroon caveat), `PriceSats` (required, ≥ 1), `Description` (optional, embedded in invoice), `IdempotencyKey` (optional, sent as `X-Idempotency-Key` header).

Response: `Invoice`, `Macaroon`, `PaymentHash`, `ExpiresAt`, `Resource`, `PriceSats`, `MppChallenge?`.

### `VerifyTokenAsync` → `Task<VerificationResult>`

Request fields: `Macaroon?` (required for L402, omit only for MPP), `Preimage` (required, hex).

Response: `Valid`, `Error?`, `Resource?`, `MerchantId?`, `AmountSats?`, `PaymentHash?`. Inspect `result.Valid` — the producer API returns 200 OK for both valid and invalid tokens.

### Exceptions

| Exception | When |
|---|---|
| `L402AuthException` | 401 — API key missing / invalid / revoked |
| `L402PlanException` | 403 — L402 not enabled on plan (surfaces `CurrentPlan`) |
| `L402ApiException` | Other non-2xx (400 / 429 / 5xx); surfaces `StatusCode` and `ResponseBody` |
| `L402NetworkException` | Timeout, DNS, TLS, transport failure (surfaces `InnerException`) |

All extend `L402ServerException`.

## Two integration modes

Lightning Enable supports two integration shapes:

- **Proxy mode** — point Lightning Enable at your API URL; we forward authenticated requests on your behalf. Best for public APIs or quick experiments. [Setup walkthrough](https://docs.lightningenable.com/products/l402-microtransactions/proxy-setup-walkthrough).
- **Native mode** — install this SDK in your existing API. Lightning Enable handles payment; your API handles everything else. Best for commercial APIs with their own auth, observability, or sensitive infrastructure. **This SDK is the Native mode building block.**

Framework-specific middleware that wraps this SDK is in development:

- `L402Server.AspNetCore` — ASP.NET Core middleware (in development)
- `l402-express` — Express middleware ([npm](https://www.npmjs.com/package/l402-express))

## Architectural notes

- **No protocol code in the SDK.** Macaroon signing, preimage hashing, payment-hash linking — all server-side.
- **Verification via the hosted endpoint, not local key material.** We don't distribute the L402 root key to merchants. Every `VerifyTokenAsync` call goes to `/api/l402/challenges/verify`. One round-trip per paid request (~50ms regional).
- **Replay prevention centralized.** Lightning Enable tracks consumed preimages; merchants don't maintain a local cache.
- **No credentials stored anywhere.** Lightning Enable never asks for your upstream API credentials.

## Contributing

Open source under MIT. Issues and pull requests welcome. For protocol-level discussion, see the [L402 spec at lightninglabs/L402](https://github.com/lightninglabs/L402).

## License

MIT © Refined Element
