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
}
