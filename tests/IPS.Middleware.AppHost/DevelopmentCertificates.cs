using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace IPS.Middleware.AppHost;

// The throwaway certificates of one run: the simulators' TLS server certificate (which the service trusts by file) and the key
// the simulated IPS signs its answers with (whose public part the service trusts as the IPS signature). Test support only.
internal sealed class DevelopmentCertificates
{
    private DevelopmentCertificates(string directory, string password)
    {
        Directory = directory;
        Password = password;
    }

    internal string Directory { get; }
    internal string Password { get; }
    internal string ServerPfx => Path.Combine(Directory, "simulators-server.pfx");
    // Only the public parts, which is all a container needs to see.
    internal string TrustDirectory => Path.Combine(Directory, "trust");
    internal string ServerPem => Path.Combine(TrustDirectory, "simulators-server.pem");
    internal string IpsSigningPfx => Path.Combine(Directory, "ips-signing.pfx");
    internal string IpsSigningPem => Path.Combine(TrustDirectory, "ips-signing.pem");

    internal static DevelopmentCertificates Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ips-aspire-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Path.Combine(directory, "trust"));
        var certificates = new DevelopmentCertificates(directory, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
        certificates.WriteServerCertificate();
        certificates.WriteIpsSigningCertificate();
        return certificates;
    }

    // Reachable from the host (localhost) and from a container (aspire.dev.internal or host.docker.internal).
    private void WriteServerCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=IPS simulators", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddDnsName("host.docker.internal");
        // What Aspire calls the host from inside a container.
        names.AddDnsName("aspire.dev.internal");
        names.AddIpAddress(IPAddress.Loopback);
        names.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        File.WriteAllBytes(ServerPfx, certificate.Export(X509ContentType.Pfx, Password));
        File.WriteAllText(ServerPem, certificate.ExportCertificatePem());
    }

    private void WriteIpsSigningCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Simulated IPS", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        File.WriteAllBytes(IpsSigningPfx, certificate.Export(X509ContentType.Pfx, Password));
        File.WriteAllText(IpsSigningPem, certificate.ExportCertificatePem());
    }
}
