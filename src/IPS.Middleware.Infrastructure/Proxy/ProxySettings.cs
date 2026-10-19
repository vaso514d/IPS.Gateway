using IPS.Middleware.Infrastructure.Transport;

namespace IPS.Middleware.Infrastructure.Proxy;

public sealed class ProxySettings
{
    public bool Enabled { get; init; }
    public string ProtocolVersion { get; init; } = "1";
    public HttpEndpointSettings Endpoint { get; init; } = new() { RequestTimeout = TimeSpan.FromSeconds(15) };
    // Optional: without a certificate the message is sent unsigned, as in the source.
    public CertificateSettings? SigningCertificate { get; init; }

    public void Validate(bool development)
    {
        if (!Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ProtocolVersion) || ProtocolVersion.Any(c => c < 33 || c > 126))
        {
            throw new InvalidOperationException("Proxy protocol version must be a nonempty ASCII header value.");
        }

        Endpoint.Validate(development);
        Endpoint.RequireClientCertificate(development, "Proxy");
    }
}
