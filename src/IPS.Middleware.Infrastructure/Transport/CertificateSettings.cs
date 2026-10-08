using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace IPS.Middleware.Infrastructure.Transport;

// A certificate configured from exactly one source: a file (PFX or PEM), a Windows store thumbprint, or the certificate
// itself in configuration (base64 PKCS#12 or PEM text), which needs no file system or store, as in a container.
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

    // PFX/P12 bytes in base64, with Password when the PKCS#12 is protected.
    public string? Pkcs12Base64 { get; init; }

    // PEM certificate text; the private key is KeyPem or, when absent, in the same text. Password decrypts an encrypted key.
    public string? Pem { get; init; }
    public string? KeyPem { get; init; }

    private bool FromStore => !string.IsNullOrWhiteSpace(Thumbprint);

    private bool FromPkcs12Text => !string.IsNullOrWhiteSpace(Pkcs12Base64);

    private bool FromPemText => !string.IsNullOrWhiteSpace(Pem);

    private bool IsPkcs12 => FromPkcs12Text || (!string.IsNullOrWhiteSpace(Path)
        && System.IO.Path.GetExtension(Path) is { } extension
        && (extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".p12", StringComparison.OrdinalIgnoreCase)));

    public X509Certificate2 Load(bool privateKeyRequired, DateTimeOffset now, bool forTls = false) =>
        Load(privateKeyRequired, now, forTls, notYetValidAccepted: false);

    // Annex C 2.1: the next IPS signature certificate can be configured before its validity starts, so the service is
    // ready when IPS switches to it. It verifies nothing until then; an expired one is still refused.
    internal X509Certificate2 LoadSignatureTrust(DateTimeOffset now) =>
        Load(privateKeyRequired: false, now, forTls: false, notYetValidAccepted: true);

    private X509Certificate2 Load(bool privateKeyRequired, DateTimeOffset now, bool forTls, bool notYetValidAccepted)
    {
        string?[] sources = [Path, Thumbprint, Pkcs12Base64, Pem];
        if (sources.Count(source => !string.IsNullOrWhiteSpace(source)) != 1)
        {
            throw new InvalidOperationException("Specify exactly one certificate source: Path, Thumbprint, Pkcs12Base64 or Pem.");
        }

        if ((KeyPath is not null && string.IsNullOrWhiteSpace(Path)) || (KeyPem is not null && !FromPemText))
        {
            throw new InvalidOperationException("KeyPath belongs to a Path source and KeyPem to a Pem source.");
        }

        var certificate = LoadSource(privateKeyRequired, forTls);
        if (forTls && OperatingSystem.IsWindows() && !FromStore && !IsPkcs12)
        {
            certificate = PrepareWindowsTls(certificate);
        }

        try
        {
            RequireUsable(certificate, privateKeyRequired, now, notYetValidAccepted);
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

        if (FromPkcs12Text)
        {
            return LoadPkcs12Text(forTls);
        }

        if (FromPemText)
        {
            return LoadPemText(privateKeyRequired);
        }

        return IsPkcs12 ? LoadPkcs12(forTls) : LoadPem(privateKeyRequired);
    }

    private static void RequireUsable(X509Certificate2 certificate, bool privateKeyRequired, DateTimeOffset now, bool notYetValidAccepted)
    {
        var expired = now > certificate.NotAfter.ToUniversalTime();
        var refusedBeforeStart = !notYetValidAccepted && now < certificate.NotBefore.ToUniversalTime();
        if (expired || refusedBeforeStart)
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
            throw new PlatformNotSupportedException("Certificate store sources require Windows; use Path, Pkcs12Base64 or Pem elsewhere.");
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

        return X509CertificateLoader.LoadPkcs12FromFile(Path!, Password, KeyStorage(forTls));
    }

    private X509Certificate2 LoadPkcs12Text(bool forTls)
    {
        byte[] pkcs12;
        try
        {
            pkcs12 = Convert.FromBase64String(Pkcs12Base64!.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Pkcs12Base64 is not valid base64.");
        }

        try
        {
            return X509CertificateLoader.LoadPkcs12(pkcs12, Password, KeyStorage(forTls));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs12);
        }
    }

    private X509Certificate2 LoadPemText(bool privateKeyRequired)
    {
        if (privateKeyRequired)
        {
            var key = KeyPem ?? Pem!;
            return Password is null
                ? X509Certificate2.CreateFromPem(Pem, key)
                : X509Certificate2.CreateFromEncryptedPem(Pem, key, Password);
        }

        if (KeyPem is not null || Password is not null)
        {
            throw new InvalidOperationException("Trust certificates require public certificate text only.");
        }

        return X509Certificate2.CreateFromPem(Pem);
    }

    // Schannel needs a persisted key for TLS on Windows; everything else keeps the key in memory only.
    private static X509KeyStorageFlags KeyStorage(bool forTls) => forTls && OperatingSystem.IsWindows()
        ? X509KeyStorageFlags.DefaultKeySet
        : X509KeyStorageFlags.EphemeralKeySet;

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
