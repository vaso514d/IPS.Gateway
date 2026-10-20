# Specification 021: authentication between the core system and the gateway

Status: implemented on main on 2026-10-20 and awaiting owner review (uncommitted). The owner set the direction on 2026-10-20.

## Owner decisions

1. **Content protection.** The signature covers the SHA-256 of the whole JSON body.
2. **Algorithms.** RSA and ECDSA are both supported; the algorithm is configurable.
3. **Responses are not signed.**
4. **Direction.** The core system signs its requests to the gateway with its certificate. The gateway calls the core system (deliveries, status callbacks, payment-status queries) with a static API token.
5. **Open when not configured.** Without trusted certificates the gateway accepts unsigned requests; without a token it sends none, and a receiver without tokens accepts every call.

The core system's own simulator (`samples/IPS.MiidleWear.CoreAPI`) uses the library, so the whole exchange can be tested end to end.

## The signature (core system → gateway)

| Step | Value |
|---|---|
| 1 | `digest = Base64(SHA-256(body))`: the exact bytes sent, 32 bytes, 44 Base64 characters. An empty body (GET) hashes zero bytes |
| 2 | `signature = Base64(Sign(ASCII(digest)))` with the core system's private key |
| 3 | Headers: `X-Ips-Content-SHA256: digest`, `X-Ips-Signature: signature`, `X-Ips-Signature-Algorithm: RS256 \| PS256 \| ES256`, `X-Ips-Certificate: SHA-1 thumbprint` |

| Algorithm | Key | Signature |
|---|---|---|
| `RS256` | RSA | RSASSA-PKCS1-v1_5 with SHA-256 |
| `PS256` | RSA | RSASSA-PSS with SHA-256 |
| `ES256` | ECDSA | ECDSA with SHA-256, IEEE P1363 (r‖s) |

Protection is declared per endpoint (owner decision, 2026-10-20): a controller or action marked `[RequireCoreSignature]` is checked by the `IpsSignature` authentication scheme, and anything unmarked stays open, such as health checks. The payment controller (sends, status query) and the proxy controller are marked. The gateway checks a marked request when `CoreAuthentication:TrustedCertificates` lists at least one certificate:

1. All four headers are present, and the algorithm is allowed (`CoreAuthentication:Algorithms`; all three by default).
2. The digest equals the SHA-256 of the body received.
3. The thumbprint names a trusted certificate, valid now.
4. The signature verifies with that certificate's public key.

Any failure answers 401 ProblemDetails, without saying which check failed. The log records the reason and the thumbprint. Two trusted certificates can be listed at once, so a certificate can be replaced without downtime.

**Limit.** Only the body is signed, as decided. A captured request can be sent again unchanged; a send repeated with the same `clientReference` returns the existing transaction and starts nothing, but a read (status query) could be repeated. Method, path and time are not covered.

## The token (gateway → core system)

- `Payments:Incoming:Transport:Cbs:ApiToken` and `Payments:Outgoing:Transport:Cbs:ApiToken` are sent as `Authorization: Bearer <token>` on every call the gateway makes to the core system.
- The receiver package checks it with the `IpsApiToken` authentication scheme: `AddIpsApiTokenAuthentication(options => options.Tokens.Add(...))`, then `.RequireIpsApiToken()` on endpoints (`MapIpsCoreReceiver().RequireIpsApiToken()`) or `[RequireIpsApiToken]` on controllers. Unmarked endpoints stay open. It accepts any of the listed tokens (rotation) and compares in constant time. Without a matching token it answers 401, which the gateway treats as no answer and retries as before.
- The token never appears in logs.

## Packages

- **`IPS.MiidleWear.Security`** (`net8.0`, base class library only, packable) holds:
  - the header names;
  - `IpsContentSignature`: digest, sign, verify;
  - `IpsSignatureVerifier`;
  - `IpsSigningHandler` and `IpsApiTokenHandler` for any `HttpClient`;
  - `IpsApiToken`;
  - `IpsCertificates`: a thumbprint in the Windows stores, or a PFX file for containers.
- **`IPS.MiidleWear.AspNetCore`** references it for the `IpsApiToken` scheme, `RequireIpsApiToken()` and `[RequireIpsApiToken]`.
- **Gateway.** Infrastructure adds the token handler to the two CBS HTTP clients. Api checks signatures with the `IpsSignature` scheme on endpoints marked `[RequireCoreSignature]`.

## Acceptance scenarios

1. Trusted certificates configured: an unsigned send answers 401 and stores nothing; a signed one is processed.
2. A body changed after signing, an unknown or expired certificate, a wrong signature or a disallowed algorithm answers 401.
3. No trusted certificates: unsigned requests are processed as before.
4. With `ApiToken` set, the gateway's deliveries carry `Authorization: Bearer`; a receiver that requires the token accepts them and answers 401 to a call without it.
5. RS256, PS256 and ES256 each sign and verify; a signature made by `IpsSigningHandler` verifies over the bytes actually sent.
