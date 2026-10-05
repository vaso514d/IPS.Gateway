using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace IPS.Middleware.Infrastructure.Transport;

// A certificate configured either as a file (PFX or PEM) or as a Windows store thumbprint.
public sealed class CertificateSettings
{
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";
    private const string AnyPurposeOid = "2.5.29.37.0";

    public string? Path { get; init; }
    public string? KeyPath { get; init; }
    public string? Password { get; init; }
    public string? Thumbprint { get; init; }
    public StoreLocation StoreLocation { get; init; } = StoreLocation.LocalMachine;
    public StoreName StoreName { get; init; } = StoreName.My;

    private bool FromStore => !string.IsNullOrWhiteSpace(Thumbprint);

    private bool IsPkcs12File => !FromStore
        && System.IO.Path.GetExtension(Path) is { } extension
        && (extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".p12", StringComparison.OrdinalIgnoreCase));

    public X509Certificate2 Load(bool privateKeyRequired, DateTimeOffset now, bool forTls = false)
    {
        if (string.IsNullOrWhiteSpace(Path) == string.IsNullOrWhiteSpace(Thumbprint))
        {
            throw new InvalidOperationException("Specify exactly one certificate file or store thumbprint.");
        }

        var certificate = LoadSource(privateKeyRequired, forTls);
        if (forTls && OperatingSystem.IsWindows() && !FromStore && !IsPkcs12File)
        {
            certificate = PrepareWindowsTls(certificate);
        }

        try
        {
            RequireUsable(certificate, privateKeyRequired, now);
            return certificate;
        }
        catch
        {
            certificate.Dispose();
            throw;
        }
    }

    internal static void RequireDigitalSignature(X509Certificate2 certificate)
    {
        var usage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (usage is not null && (usage.KeyUsages & X509KeyUsageFlags.DigitalSignature) == 0)
        {
            throw new InvalidOperationException("Certificate must permit digital signatures.");
        }
    }

    internal static void RequireClientAuthentication(X509Certificate2 certificate)
    {
        RequireDigitalSignature(certificate);
        var enhancedUsage = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        var permitsClientAuthentication = enhancedUsage is null || enhancedUsage.EnhancedKeyUsages
            .Cast<Oid>()
            .Any(oid => oid.Value is ClientAuthenticationOid or AnyPurposeOid);
        if (!permitsClientAuthentication)
        {
            throw new InvalidOperationException("TLS certificate must permit client authentication.");
        }
    }

    private X509Certificate2 LoadSource(bool privateKeyRequired, bool forTls)
    {
        if (FromStore)
        {
            return LoadFromStore();
        }

        return IsPkcs12File ? LoadPkcs12(forTls) : LoadPem(privateKeyRequired);
    }

    private static void RequireUsable(X509Certificate2 certificate, bool privateKeyRequired, DateTimeOffset now)
    {
        if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
        {
            throw new InvalidOperationException("Configured certificate is outside its validity period.");
        }

        if (privateKeyRequired && !certificate.HasPrivateKey)
        {
            throw new InvalidOperationException("Configured certificate requires a private key.");
        }
    }

    private X509Certificate2 LoadFromStore()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Certificate store sources require Windows.");
        }

        if (KeyPath is not null || Password is not null)
        {
            throw new InvalidOperationException("Store certificates cannot specify file credentials.");
        }

        using var store = new X509Store(StoreName, StoreLocation);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, Thumbprint!, validOnly: false);
        try
        {
            if (matches.Count != 1)
            {
                throw new InvalidOperationException("Certificate thumbprint must identify exactly one certificate.");
            }

            return new X509Certificate2(matches[0]);
        }
        finally
        {
            foreach (var match in matches)
            {
                match.Dispose();
            }
        }
    }

    private X509Certificate2 LoadPkcs12(bool forTls)
    {
        if (KeyPath is not null)
        {
            throw new InvalidOperationException("PFX files cannot specify a separate key.");
        }

        var keyStorage = forTls && OperatingSystem.IsWindows()
            ? X509KeyStorageFlags.DefaultKeySet
            : X509KeyStorageFlags.EphemeralKeySet;
        return X509CertificateLoader.LoadPkcs12FromFile(Path!, Password, keyStorage);
    }

    private X509Certificate2 LoadPem(bool privateKeyRequired)
    {
        if (privateKeyRequired)
        {
            var keyPath = KeyPath ?? Path!;
            return Password is null
                ? X509Certificate2.CreateFromPemFile(Path!, keyPath)
                : X509Certificate2.CreateFromEncryptedPemFile(Path!, Password, keyPath);
        }

        if (KeyPath is not null || Password is not null)
        {
            throw new InvalidOperationException("Trust certificates require public certificate files only.");
        }

        return X509CertificateLoader.LoadCertificateFromFile(Path!);
    }

    // Schannel requires a named key. DefaultKeySet creates a temporary key deleted on disposal.
    private static X509Certificate2 PrepareWindowsTls(X509Certificate2 certificate)
    {
        using var pem = certificate;
        var pkcs12 = pem.Export(X509ContentType.Pfx);
        try
        {
            return X509CertificateLoader.LoadPkcs12(pkcs12, null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs12);
        }
    }
}
