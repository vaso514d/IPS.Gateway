using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Transport;

namespace IPS.Middleware.Infrastructure.Proxy;

// The Proxy Solution's own certificates: an optional signing certificate and the TLS client and server trust of its endpoint.
public sealed class ProxyTransportCertificates(ProxySettings settings, Pacs008SigningPolicy policy, TimeProvider time)
    : ISigningCertificateSource, ICertificateExpirySource, IDisposable
{
    private readonly TransportCertificates _certificates =
        new(settings.SigningCertificate, settings.Endpoint, new HttpEndpointSettings(), [], policy, time, signingOptional: true);

    public ValueTask<X509Certificate2?> GetCurrentAsync(CancellationToken cancellationToken) => _certificates.GetCurrentAsync(cancellationToken);

    public IReadOnlyList<CertificateExpiry> Expiries() => _certificates.Expiries("proxy");

    public void Dispose() => _certificates.Dispose();

    internal SslClientAuthenticationOptions Tls(bool revocation) => _certificates.Tls(ips: true, revocation);
}
