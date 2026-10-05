using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Transport;

namespace IPS.Middleware.Infrastructure.Payments.Transport;

public sealed class OutgoingTransportCertificates : ISigningCertificateSource, IDisposable
{
    private readonly TransportCertificates certificates;
    public OutgoingTransportCertificates(OutgoingTransportSettings settings, Pacs008SigningPolicy policy, TimeProvider time)
    {
        certificates = new(settings.SigningCertificate, settings.Ips, settings.Cbs, settings.IpsSignatureTrust, policy, time);
    }
    public IReadOnlyCollection<X509Certificate2> IpsSignatureTrust => certificates.IpsSignatureTrust;
    public ValueTask<X509Certificate2?> GetCurrentAsync(CancellationToken token) => certificates.GetCurrentAsync(token);
    internal SslClientAuthenticationOptions Tls(bool ips, bool revocation) => certificates.Tls(ips, revocation);
    public void Dispose() => certificates.Dispose();
}
