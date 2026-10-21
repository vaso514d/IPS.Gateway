using IPS.MiidleWear.Security;

namespace IPS.Middleware.Infrastructure.Transport;

public sealed class HttpEndpointSettings
{
    public string BaseUrl { get; init; } = "";
    public int ConnectionLimit { get; init; } = 100;
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan PooledConnectionLifetime { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan PooledConnectionIdleTimeout { get; init; } = TimeSpan.FromMinutes(1);
    public CircuitBreakerSettings CircuitBreaker { get; init; } = new();
    public CertificateSettings? ClientCertificate { get; init; }
    public CertificateSettings[] ServerTrust { get; init; } = [];
    public bool CheckCertificateRevocation { get; init; } = true;

    // The core system's endpoints only (specification 021): what the gateway sends with every call to the core system.
    // ApiToken goes as Authorization: Bearer, and Signing is the gateway's own certificate (with its private key) that
    // signs each request body; each is left out when not configured. What the gateway requires of the core system's
    // requests is in the Server section.
    public string? ApiToken { get; init; }
    public IpsSigningSettings? Signing { get; init; }

    public void Validate(bool development)
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(development && uri.Scheme == "http" && uri.IsLoopback)) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new InvalidOperationException("Transport URL must be HTTPS (loopback HTTP is allowed only in Development).");
        }

        if (ConnectionLimit <= 0 || ConnectTimeout <= TimeSpan.Zero || RequestTimeout <= ConnectTimeout ||
            RequestTimeout > TimeSpan.FromDays(1) || PooledConnectionLifetime <= TimeSpan.Zero || PooledConnectionIdleTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Invalid HTTP connection or timeout limits.");
        }

        CircuitBreaker.Validate();
    }

    // The token and the request signature are for the core system: anywhere else they fail startup instead of being
    // ignored (IPS and the Proxy Solution authenticate by TLS and their own XML signature).
    internal void RequireNoCoreAuthentication(string endpoint)
    {
        if (!string.IsNullOrWhiteSpace(ApiToken) || Signing is { IsConfigured: true })
        {
            throw new InvalidOperationException($"{endpoint}: ApiToken and Signing belong to the Cbs endpoints.");
        }
    }

    // A configured signing algorithm must be known at startup; the certificate is loaded with the HTTP client.
    internal void ValidateCoreAuthentication()
    {
        if (Signing is { IsConfigured: true } signing)
        {
            _ = signing.SignatureAlgorithm();
        }
    }

    // IPS authenticates the participant by its TLS client certificate and answers 401 without one (Annex D 6.2), so
    // every real endpoint requires it in every environment. Only a loopback simulator in Development runs without one.
    internal void RequireClientCertificate(bool development, string service)
    {
        var simulator = development && Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && uri.IsLoopback;
        if (ClientCertificate is null && !simulator)
        {
            throw new InvalidOperationException($"{service} mutual TLS requires a client certificate (only a loopback simulator in Development runs without one).");
        }
    }
}
