using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace IPS.Middleware.Infrastructure.Transport;

public sealed class CertificateSettings
{
    public string? Path { get; init; }
    public string? KeyPath { get; init; }
    public string? Password { get; init; }
    public string? Thumbprint { get; init; }
    public StoreLocation StoreLocation { get; init; } = StoreLocation.LocalMachine;
    public StoreName StoreName { get; init; } = StoreName.My;

    public X509Certificate2 Load(bool privateKeyRequired, DateTimeOffset now, bool forTls = false)
    {
        if (string.IsNullOrWhiteSpace(Path) == string.IsNullOrWhiteSpace(Thumbprint))
            throw new InvalidOperationException("Specify exactly one certificate file or store thumbprint.");
        var store = !string.IsNullOrWhiteSpace(Thumbprint);
        var pfx = !store && System.IO.Path.GetExtension(Path) is { } extension &&
            (extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".p12", StringComparison.OrdinalIgnoreCase));
        X509Certificate2 certificate;
        if (store)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Certificate store sources require Windows.");
            if (KeyPath is not null || Password is not null) throw new InvalidOperationException("Store certificates cannot specify file credentials.");
            using var certificates = new X509Store(StoreName, StoreLocation);
            certificates.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            var matches = certificates.Certificates.Find(X509FindType.FindByThumbprint, Thumbprint!, validOnly: false);
            try
            {
                if (matches.Count != 1) throw new InvalidOperationException("Certificate thumbprint must identify exactly one certificate.");
                certificate = new X509Certificate2(matches[0]);
            }
            finally { foreach (var match in matches) match.Dispose(); }
        }
        else if (pfx)
        {
            if (KeyPath is not null) throw new InvalidOperationException("PFX files cannot specify a separate key.");
            certificate = X509CertificateLoader.LoadPkcs12FromFile(Path!, Password, forTls && OperatingSystem.IsWindows() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet);
        }
        else if (privateKeyRequired)
        {
            certificate = Password is null
                ? X509Certificate2.CreateFromPemFile(Path!, KeyPath ?? Path!)
                : X509Certificate2.CreateFromEncryptedPemFile(Path!, Password, KeyPath ?? Path!);
        }
        else
        {
            if (KeyPath is not null || Password is not null) throw new InvalidOperationException("Trust certificates require public certificate files only.");
            certificate = X509CertificateLoader.LoadCertificateFromFile(Path!);
        }
        if (forTls && OperatingSystem.IsWindows() && !store && !pfx)
        {
            // Schannel requires a named key. DefaultKeySet creates a temporary key deleted on disposal.
            using var pem = certificate;
            var pkcs12 = pem.Export(X509ContentType.Pfx);
            try { certificate = X509CertificateLoader.LoadPkcs12(pkcs12, null); }
            finally { CryptographicOperations.ZeroMemory(pkcs12); }
        }
        try
        {
            if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
                throw new InvalidOperationException("Configured certificate is outside its validity period.");
            if (privateKeyRequired && !certificate.HasPrivateKey)
                throw new InvalidOperationException("Configured certificate requires a private key.");
            return certificate;
        }
        catch { certificate.Dispose(); throw; }
    }

    internal static void RequireDigitalSignature(X509Certificate2 certificate)
    {
        var usage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (usage is not null && (usage.KeyUsages & X509KeyUsageFlags.DigitalSignature) == 0)
            throw new InvalidOperationException("Certificate must permit digital signatures.");
    }

    internal static void RequireClientAuthentication(X509Certificate2 certificate)
    {
        RequireDigitalSignature(certificate);
        var eku = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        // TLS client authentication, or any purpose.
        if (eku is not null && !eku.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value is "1.3.6.1.5.5.7.3.2" or "2.5.29.37.0"))
            throw new InvalidOperationException("TLS certificate must permit client authentication.");
    }
}
