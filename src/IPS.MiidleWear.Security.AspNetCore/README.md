# IPS.MiidleWear.Security.AspNetCore

The server side of `IPS.MiidleWear.Security` for ASP.NET Core, used by the IPS middleware (the gateway) and by core banking systems. A service requires what its `Server` section configures, on the endpoints it marks:

```json
"Server": {
  "Url": "http://localhost:5280",
  "ApiTokens": [ "token-the-caller-sends" ],
  "Signing": {
    "TrustedCertificates": [ { "Thumbprint": "<the caller's public certificate>" } ],
    "Algorithms": []
  }
}
```

| Configured | Required on a marked endpoint |
|---|---|
| `ApiTokens` | `Authorization: Bearer <token>` with one of them |
| `Signing:TrustedCertificates` | a signature of the body by one of them (`X-Ips-*` headers, see `IPS.MiidleWear.Security`) |
| both | both |
| neither | nothing: the endpoint is open |

- `Url` is where the service listens; empty keeps the host's own setting.
- A certificate is a `Thumbprint` (the Windows stores), a `Path` (a `.cer`/`.pem`; the public certificate is enough) or `Pem` text.
- `Algorithms` limits `RS256`, `PS256` and `ES256`; empty accepts all three.
- List two tokens or certificates while one is replaced.

```csharp
builder.AddIpsServer();                                   // Server:Url and the authentication
var app = builder.Build();
app.MapIpsCoreReceiver().RequireIpsAuthentication();      // or [RequireIpsAuthentication] on a controller or action
app.MapGet("/health", () => "Healthy");                   // not marked: open
```

A request that fails answers 401 without saying why; the log has the reason and never the token. The gateway treats a 401 from a core system as no answer and asks again.
