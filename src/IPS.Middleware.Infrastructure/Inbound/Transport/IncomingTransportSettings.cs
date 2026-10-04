using IPS.MiidleWear.Contracts.Pacs008;
using IPS.MiidleWear.Contracts.Transactions;

namespace IPS.Middleware.Infrastructure.Inbound.Transport;

public sealed class IncomingTransportSettings
{
    public bool Enabled { get; init; }
    public string ParticipantBic { get; init; } = "";
    public HttpEndpointSettings Ips { get; init; } = new();
    public HttpEndpointSettings Cbs { get; init; } = new();
    public string IpsVersion { get; init; } = "1";
    public string MessagePath { get; init; } = "Message";
    public string SubmissionPath { get; init; } = Pacs008RestApiRoutes.Receive;
    public string StatusPath { get; init; } = TransactionRestApiRoutes.PaymentStatus;
    public string ReversalPath { get; init; } = TransactionRestApiRoutes.ReceiveStatus;
    public TimeSpan ReceiveTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public CertificateSettings? SigningCertificate { get; init; }
    public CertificateSettings[] IpsSignatureTrust { get; init; } = [];

    public void Validate(bool development)
    {
        if (!Enabled) return;
        if (ParticipantBic.Length is not (8 or 11) || ParticipantBic.Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new InvalidOperationException("Incoming transport requires an 8 or 11 character participant BIC.");
        if (string.IsNullOrWhiteSpace(IpsVersion) || IpsVersion.Any(c => c < 33 || c > 126))
            throw new InvalidOperationException("IPS version must be a nonempty ASCII header value.");
        Ips.Validate(development);
        Cbs.Validate(development);
        foreach (var path in new[] { MessagePath, SubmissionPath, StatusPath, ReversalPath })
        {
            if (string.IsNullOrWhiteSpace(path) || path.Contains('?') || path.Contains('#') || path.Contains(':') || path.Contains('\\') ||
                !Uri.TryCreate(path.TrimStart('/'), UriKind.Relative, out _) || path.StartsWith("//", StringComparison.Ordinal))
                throw new InvalidOperationException("Transport paths must be relative paths without query or fragment.");
        }
        if (ReceiveTimeout <= Ips.ConnectTimeout || ReceiveTimeout > TimeSpan.FromDays(1))
            throw new InvalidOperationException("Receive timeout must exceed the IPS connection timeout and be at most one day.");
        if (Ips.ConnectionLimit < 2 || Cbs.ConnectionLimit < 3)
            throw new InvalidOperationException("IPS requires a receive reservation; CBS requires two follow-up reservations.");
        if (IpsSignatureTrust.Length == 0)
            throw new InvalidOperationException("IPS signature trust certificates are required.");
        if (!development && Ips.ClientCertificate is null)
            throw new InvalidOperationException("IPS mutual TLS requires a client certificate.");
    }
}

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
            throw new InvalidOperationException("Transport URL must be HTTPS (loopback HTTP is allowed only in Development).");
        if (ConnectionLimit <= 0 || ConnectTimeout <= TimeSpan.Zero || RequestTimeout <= ConnectTimeout ||
            RequestTimeout > TimeSpan.FromDays(1) || PooledConnectionLifetime <= TimeSpan.Zero || PooledConnectionIdleTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("Invalid HTTP connection or timeout limits.");
        CircuitBreaker.Validate();
    }
}

public sealed class CircuitBreakerSettings
{
    public double FailureRatio { get; init; } = 0.5;
    public int MinimumThroughput { get; init; } = 10;
    public TimeSpan SamplingDuration { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(5);

    internal void Validate()
    {
        if (!double.IsFinite(FailureRatio) || FailureRatio <= 0 || FailureRatio > 1 || MinimumThroughput < 2 ||
            SamplingDuration < TimeSpan.FromMilliseconds(500) || BreakDuration < TimeSpan.FromMilliseconds(500))
            throw new InvalidOperationException("Invalid HTTP circuit-breaker settings.");
    }
}
