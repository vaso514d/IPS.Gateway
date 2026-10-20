# IPS.MiidleWear.Security

Authentication between a core banking system and the IPS middleware (the gateway). Base class library only.

| Direction | Mechanism | Core system |
|---|---|---|
| Core system → gateway | The core system signs every request body with its certificate | `IpsSigningHandler` on the `HttpClient` that calls the gateway |
| Gateway → core system | The gateway sends a static API token, `Authorization: Bearer <token>` | `AddIpsApiTokenAuthentication`, then `.RequireIpsApiToken()` or `[RequireIpsApiToken]` (package `IPS.MiidleWear.AspNetCore`) |

When the gateway has no trusted certificate configured it accepts unsigned requests; when it has no token it sends none, and a receiver given no tokens accepts every call.

## The signature

1. `digest = Base64(SHA-256(body))`: the exact bytes sent (32 bytes, 44 characters). A request without a body hashes zero bytes.
2. `signature = Base64(Sign(ASCII(digest)))` with the private key.
3. Headers:

| Header | Value |
|---|---|
| `X-Ips-Content-SHA256` | the digest |
| `X-Ips-Signature` | the signature |
| `X-Ips-Signature-Algorithm` | `RS256` (RSA PKCS#1 v1.5), `PS256` (RSA-PSS) or `ES256` (ECDSA, r‖s) |
| `X-Ips-Certificate` | the certificate's SHA-1 thumbprint, which the gateway must trust |

The gateway answers 401 when a header is missing, the digest does not match the body, the certificate is not trusted or not valid now, or the signature is wrong.

## Use

```csharp
// Windows: by thumbprint (CurrentUser, then LocalMachine). A container: a PFX file and its password.
var certificate = IpsCertificates.FromStore("B264BEB0329F4CDB55CFC84E12558CE7A9075A72");

services.AddHttpClient("gateway", client => client.BaseAddress = new Uri("https://gateway/"))
    .AddHttpMessageHandler(() => new IpsSigningHandler(certificate, IpsSignatureAlgorithm.RS256));

// The receiver accepts only calls that carry one of these tokens (two at once while a token is replaced).
services.AddIpsApiTokenAuthentication(options => options.Tokens.Add("token-from-the-gateway-operator"));
app.MapIpsCoreReceiver().RequireIpsApiToken();
```

`IpsContentSignature` and `IpsSignatureVerifier` compute and check signatures directly, for tests or another HTTP stack.
