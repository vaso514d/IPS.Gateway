using System.Security.Cryptography.X509Certificates;

namespace IPS.MiidleWear.Security;

/// <summary>
/// Loads the signing certificate (or a trusted public certificate): by thumbprint from the Windows certificate stores, or
/// from a file, as in a container.
/// </summary>
public static class IpsCertificates
{
    // The current user comes first, so a development machine uses its own copy and a server the LocalMachine one.
    private static readonly (StoreName Name, StoreLocation Location)[] Stores =
    [
        (StoreName.My, StoreLocation.CurrentUser),
        (StoreName.My, StoreLocation.LocalMachine),
        (StoreName.Root, StoreLocation.CurrentUser),
        (StoreName.Root, StoreLocation.LocalMachine),
        (StoreName.CertificateAuthority, StoreLocation.CurrentUser),
        (StoreName.CertificateAuthority, StoreLocation.LocalMachine)
    ];

    /// <summary>The certificate with <paramref name="thumbprint"/> from the first store that has it.</summary>
    /// <exception cref="InvalidOperationException">No store has it.</exception>
    public static X509Certificate2 FromStore(string thumbprint)
    {
        var wanted = new string(thumbprint.Where(char.IsAsciiHexDigit).ToArray());
        foreach (var (name, location) in Stores)
        {
            using var store = new X509Store(name, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            var found = store.Certificates.Find(X509FindType.FindByThumbprint, wanted, validOnly: false);
            if (found.Count > 0)
            {
                return found[0];
            }
        }

        throw new InvalidOperationException($"No certificate store has the certificate {wanted}.");
    }

    /// <summary>
    /// A certificate file: PFX/P12 with its private key and <paramref name="password"/>, or a public certificate
    /// (.cer/.crt, DER or PEM) to trust. The key is kept in memory only.
    /// </summary>
    public static X509Certificate2 FromFile(string path, string? password = null) =>
        new(path, password, X509KeyStorageFlags.EphemeralKeySet);
}
