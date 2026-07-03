# Changelog

All notable changes to the `L402Server` NuGet package are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## Versioning policy (0.x)

This package is pre-1.0. Per [semver](https://semver.org/#spec-item-4), 0.x
releases may include breaking changes in **minor** versions: `0.N` → `0.N+1`
can break the public API (called out explicitly in the entry); patch releases
are always backward compatible. Pin `[0.2.0,0.3.0)` if you need stability.

## [0.2.0] - 2026-07-03

### Added

- `VerifyTokenRequest.Resource` (optional): set the request path you are
  gating and the Lightning Enable producer API enforces the macaroon's
  `path` caveat server-side — a token bound to a different resource returns
  `Valid = false`. Previously the caveat value was only echoed back and
  enforcement was the integrator's responsibility.
- `VerifyTokenRequest.AmountSats` (optional): set your endpoint's price and
  the producer API enforces the macaroon's `amount_sats` caveat server-side,
  preventing replay of a cheaper token against a pricier endpoint.
- Client-side validation mirroring the producer API: empty-string `Resource`
  and `AmountSats < 1` throw `ArgumentException` immediately instead of a
  400 round-trip.
- This CHANGELOG.

### Changed

- README: `L402Server.AspNetCore` (NuGet) and `l402-express` (npm) moved
  from "forthcoming" / "in development" to shipped, with registry links.

No breaking changes — both new properties are optional and omitted from the
wire body when null.

## [0.1.2] - 2026-05-12

### Fixed

- Reject unresolved environment-variable placeholders (the literal string
  `${VAR_NAME}`) passed as `ApiKey` at construction time with a
  self-diagnosing `ArgumentException`, instead of sending them as the
  `X-API-Key` header and surfacing an opaque 401. Validation shared between
  direct construction and DI registration via `ApiKeyValidator`.

## [0.1.1] - 2026-05-12

### Fixed

- `L402ServerOptions` properties made settable so the options-lambda DI
  registration pattern (`AddL402Server(opts => ...)`) works.

## [0.1.0] - 2026-05-12

### Added

- Initial release: `L402ServerClient` wrapping Lightning Enable's hosted
  L402 producer API — `CreateChallengeAsync` (`POST /api/l402/challenges`)
  and `VerifyTokenAsync` (`POST /api/l402/challenges/verify`), typed
  exceptions (`L402AuthException`, `L402PlanException`, `L402ApiException`,
  `L402NetworkException`), `IHttpClientFactory` support via
  `AddL402Server()`, per-request timeout. Targets .NET 8.0+.
