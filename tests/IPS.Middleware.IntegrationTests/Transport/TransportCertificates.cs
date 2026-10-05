using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Transport;

namespace IPS.Middleware.IntegrationTests.Transport;

internal sealed class TransportCertificates : IDisposable
{
    private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ips-http-" + Guid.NewGuid().ToString("N"));
    private readonly List<X509Certificate2> certificates = [];
    public X509Certificate2 Root { get; }
    public X509Certificate2 Server { get; }
    public X509Certificate2 Client { get; }
    public CertificateSettings Trust { get; }
    public CertificateSettings SignatureTrust { get; }
    public CertificateSettings Identity { get; }

    public TransportCertificates()
    {
        Directory.CreateDirectory(directory);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Simulator Root", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        Root = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        certificates.Add(Root);
        Server = Leaf(server: true);
        Client = Leaf(server: false);
        Trust = SavePublic(Root, "root.pem");
        Identity = SavePfx(Client);
        SignatureTrust = SavePublic(Client, "ips-signature.pem");
    }

    public X509Certificate2 Leaf(bool server, bool wrongHost = false)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Simulator", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
            { new(server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, true));
        if (server)
        {
            var names = new SubjectAlternativeNameBuilder();
            names.AddIpAddress(wrongHost ? IPAddress.Parse("127.0.0.2") : IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
        }
        using var publicCertificate = request.Create(Root, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(16));
        using var ephemeral = publicCertificate.CopyWithPrivateKey(key);
        var certificate = X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
        certificates.Add(certificate);
        return certificate;
    }

    public CertificateSettings SavePublic(X509Certificate2 certificate, string name)
    {
        var path = System.IO.Path.Combine(directory, name);
        File.WriteAllText(path, certificate.ExportCertificatePem());
        return new() { Path = path };
    }

    public CertificateSettings SavePfx(X509Certificate2 certificate)
    {
        var path = System.IO.Path.Combine(directory, Guid.NewGuid() + ".pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, "test-only"));
        return new() { Path = path, Password = "test-only" };
    }

    public CertificateSettings SavePem(bool encrypted)
    {
        var path = System.IO.Path.Combine(directory, Guid.NewGuid() + ".pem");
        var keyPath = path + ".key";
        File.WriteAllText(path, Client.ExportCertificatePem());
        using var key = Client.GetECDsaPrivateKey()!;
        File.WriteAllText(keyPath, encrypted
            ? key.ExportEncryptedPkcs8PrivateKeyPem("test-only", new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000))
            : key.ExportPkcs8PrivateKeyPem());
        return new() { Path = path, KeyPath = keyPath, Password = encrypted ? "test-only" : null };
    }

    public void Dispose()
    {
        foreach (var certificate in certificates) certificate.Dispose();
        Directory.Delete(directory, recursive: true);
    }
}
