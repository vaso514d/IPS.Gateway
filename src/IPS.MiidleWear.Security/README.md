# IPS.MiidleWear.Security

Authentication between a core banking system and the IPS middleware (the gateway): request signatures over the JSON body, and static API tokens. Base class library only. The server side for ASP.NET Core, the `Server` section and `[RequireIpsAuthentication]`, is in `IPS.MiidleWear.Security.AspNetCore`.

Which mechanism applies is a matter of configuration. The agreed scheme: the core system signs its requests to the gateway, and the gateway calls the core system with a token. A client sends what the other side's address section configures: `ApiToken`, and `Signing` (its own certificate with the private key). What is not configured is not sent.

## The signature

1. `digest = Base64(SHA-256(body))`: the exact bytes sent (32 bytes, 44 characters). A request without a body hashes zero bytes.
2. `signature = Base64(Sign(ASCII(digest)))` with the private key.
3. Headers:

| Header | Value |
|---|---|
| `X-Ips-Content-SHA256` | the digest |
| `X-Ips-Signature` | the signature |
| `X-Ips-Signature-Algorithm` | `RS256` (RSA PKCS#1 v1.5), `PS256` (RSA-PSS) or `ES256` (ECDSA, r‖s) |
| `X-Ips-Certificate` | the certificate's SHA-1 thumbprint, which the receiver must trust |

The receiver answers 401 in any of these cases: a header is missing, the digest does not match the body, the certificate is not trusted or not valid now, or the signature is wrong.

## The token

`Authorization: Bearer <token>`. The token is any printable ASCII text without spaces and does not need to be Base64. Generate it at random, for example `[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))`.

## Use

```json
"Gateway": {
  "BaseUrl": "https://gateway.bank.local:5080",
  "ApiToken": "",
  "Signing": { "Thumbprint": "<own certificate, with its private key>", "Path": "", "Password": "", "Algorithm": "RS256" }
}
```

```csharp
services.AddHttpClient("gateway", client => client.BaseAddress = new Uri(configuration["Gateway:BaseUrl"]!))
    .AddHttpMessageHandler(() => new IpsApiTokenHandler(configuration["Gateway:ApiToken"]))        // nothing when empty
    .AddHttpMessageHandler(() => IpsSigningHandler.Create(
        configuration.GetSection("Gateway:Signing").Get<IpsSigningSettings>()));                   // unsigned when empty
```

The `Signing` certificate comes from one of two sources: a `Thumbprint` (Windows: CurrentUser, then LocalMachine), or a `Path` to a `.pfx` with its `Password` (a container). `IpsContentSignature` and `IpsSignatureVerifier` compute and check signatures directly, for tests or another HTTP stack.
