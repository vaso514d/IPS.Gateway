using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Transport;

namespace IPS.Middleware.Infrastructure.Payments.Transport;

// A distinct type per direction, so incoming and outgoing certificates are configured and owned independently.
public sealed class OutgoingTransportCertificates(OutgoingTransportSettings settings, Pacs008SigningPolicy policy, TimeProvider time)
    : ISigningCertificateSource, IDisposable
{
    private readonly TransportCertificates _certificates =
        new(settings.SigningCertificate, settings.Ips, settings.Cbs, settings.IpsSignatureTrust, policy, time);

    public IReadOnlyCollection<X509Certificate2> IpsSignatureTrust => _certificates.IpsSignatureTrust;

    public ValueTask<X509Certificate2?> GetCurrentAsync(CancellationToken token) => _certificates.GetCurrentAsync(token);

    public void Dispose() => _certificates.Dispose();

    internal SslClientAuthenticationOptions Tls(bool ips, bool revocation) => _certificates.Tls(ips, revocation);
}
