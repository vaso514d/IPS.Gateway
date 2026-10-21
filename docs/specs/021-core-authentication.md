# Specification 021: authentication between the core system and the gateway

Status: implemented on main on 2026-10-21 and awaiting owner review (uncommitted). The owner set the direction on 2026-10-20 and 2026-10-21.

## Owner decisions

1. **Content protection.** The signature covers the SHA-256 of the whole JSON body.
2. **Algorithms.** RSA and ECDSA are both supported; the algorithm is configurable.
3. **Responses are not signed.**
4. **The agreed scheme.** The core system signs its requests to the gateway with its certificate. The gateway calls the core system (deliveries, status callbacks, payment-status queries) with a static API token.
5. **Open when not configured.** What is not configured is not required and not sent.
6. **Per endpoint** (2026-10-20). Protection is declared on endpoints and controllers; unmarked ones, such as health checks, stay open.
7. **Configured, and universal** (2026-10-21). Which mechanism applies depends on configuration only. Either service can require a token, a signature, both or neither, and either client can send a token, a signature or both. A service's requirements sit next to its own address (`Server`); what a client sends sits under the other side's address.

The core system's simulator (`samples/IPS.MiidleWear.CoreAPI`) uses the same packages, so the whole exchange can be tested end to end.

## The signature

| Step | Value |
|---|---|
| 1 | `digest = Base64(SHA-256(body))`: the exact bytes sent, 32 bytes, 44 Base64 characters. An empty body (GET) hashes zero bytes |
| 2 | `signature = Base64(Sign(ASCII(digest)))` with the sender's private key |
| 3 | Headers: `X-Ips-Content-SHA256: digest`, `X-Ips-Signature: signature`, `X-Ips-Signature-Algorithm: RS256 \| PS256 \| ES256`, `X-Ips-Certificate: SHA-1 thumbprint` |

| Algorithm | Key | Signature |
|---|---|---|
| `RS256` | RSA | RSASSA-PKCS1-v1_5 with SHA-256 |
| `PS256` | RSA | RSASSA-PSS with SHA-256 |
| `ES256` | ECDSA | ECDSA with SHA-256, IEEE P1363 (r‖s) |

A receiver with trusted certificates checks four things:

1. All four headers are present, and the algorithm is allowed.
2. The digest equals the SHA-256 of the body received.
3. The thumbprint names a trusted certificate that is valid now.
4. The signature verifies with that certificate's public key.

## The token

`Authorization: Bearer <token>`. The token is opaque printable ASCII and does not need to be Base64. It is compared in constant time against every accepted token, so two can be accepted while one is replaced. It never appears in logs.

## Configuration

| Section | Holds |
|---|---|
| `Server` (each service) | `Url` (where it listens; empty keeps the host's setting), `ApiTokens` (accepted), `Signing:TrustedCertificates` and `Signing:Algorithms` (accepted signatures) |
| Gateway: `Payments:Incoming:Transport:Cbs`, `Payments:Outgoing:Transport:Cbs` | `BaseUrl`, `ApiToken` (sent), `Signing` (own certificate with private key, and `Algorithm`) |
| CoreAPI: `Gateway` | `BaseUrl`, `ApiToken` (sent), `Signing` (own certificate with private key, and `Algorithm`) |

A marked endpoint requires everything `Server` configures: a token, a signature, both, or nothing. Any failure answers 401 ProblemDetails without saying which check failed; the log has the reason.

`ApiToken` and `Signing` under the gateway's IPS or proxy endpoints stop startup.

**Limit.** Only the body is signed, as decided. A captured request can be sent again unchanged. A send repeated with the same `clientReference` returns the existing transaction and starts nothing, but a read (status query) could be repeated. Method, path and time are not covered.

## Packages

- **`IPS.MiidleWear.Security`** (`net8.0`, base class library only, packable):
  - the headers;
  - `IpsContentSignature` and `IpsSignatureVerifier`;
  - `IpsSigningHandler` (also `Create(IpsSigningSettings)`) and `IpsApiTokenHandler` for any `HttpClient`;
  - `IpsApiToken`;
  - `IpsCertificates`, `IpsCertificateSettings` and `IpsSigningSettings`.
- **`IPS.MiidleWear.Security.AspNetCore`** (`net8.0`, Security and the ASP.NET Core shared framework, packable):
  - `AddIpsServer()`, which reads the `Server` section and registers the `IpsServer` authentication scheme;
  - `[RequireIpsAuthentication]` and `.RequireIpsAuthentication()`.
- **Gateway.**
  - Api calls `AddIpsServer()` and marks `OutgoingPaymentsController` and `ProxyController`.
  - Infrastructure adds the token and signing handlers to every named HTTP client from its endpoint settings. Only the CBS endpoints may configure them.
- **`IPS.MiidleWear.AspNetCore`** is unchanged. A core system combines `MapIpsCoreReceiver()` with `.RequireIpsAuthentication()`.

## Acceptance scenarios

1. A gateway that trusts a certificate refuses an unsigned request, or one signed by another certificate, with 401, and processes a request signed by the trusted one. Health stays open.
2. A body changed after signing answers 401.
3. A gateway that requires a token and a signature refuses either alone and accepts both.
4. With nothing configured, requests are processed as before.
5. A core receiver that requires tokens accepts a listed token, and refuses a wrong or missing one with 401.
6. A core receiver that trusts the gateway's certificate accepts the gateway's signed calls (`Cbs:Signing`) and refuses unsigned ones.
7. RS256, PS256 and ES256 each sign and verify; a signature made by `IpsSigningHandler` verifies over the bytes actually sent.
