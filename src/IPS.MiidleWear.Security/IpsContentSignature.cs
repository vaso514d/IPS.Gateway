using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace IPS.MiidleWear.Security;

/// <summary>
/// The content signature: <c>digest = Base64(SHA-256(body))</c>, then <c>signature = Base64(Sign(ASCII(digest)))</c>.
/// The body is the exact bytes sent; an empty body hashes zero bytes.
/// </summary>
public static class IpsContentSignature
{
    /// <summary>Base64 of the SHA-256 of <paramref name="content"/>: 32 bytes, 44 characters.</summary>
    public static string Digest(ReadOnlySpan<byte> content) => Convert.ToBase64String(SHA256.HashData(content));

    /// <summary>Signs <paramref name="digest"/> with the certificate's private key.</summary>
    /// <exception cref="InvalidOperationException">The certificate has no private key of the algorithm's type.</exception>
    public static string Sign(string digest, X509Certificate2 certificate, IpsSignatureAlgorithm algorithm)
    {
        var data = Encoding.ASCII.GetBytes(digest);
        if (algorithm == IpsSignatureAlgorithm.ES256)
        {
            using var ecdsa = certificate.GetECDsaPrivateKey() ?? throw MissingKey(certificate, algorithm);
            return Convert.ToBase64String(ecdsa.SignData(data, HashAlgorithmName.SHA256));
        }

        using var rsa = certificate.GetRSAPrivateKey() ?? throw MissingKey(certificate, algorithm);
        return Convert.ToBase64String(rsa.SignData(data, HashAlgorithmName.SHA256, Padding(algorithm)));
    }

    /// <summary>
    /// Whether <paramref name="signature"/> is a valid signature of <paramref name="digest"/> by the certificate's public
    /// key. Malformed Base64 and a key of the wrong type are invalid.
    /// </summary>
    public static bool Verify(string digest, string signature, IpsSignatureAlgorithm algorithm, X509Certificate2 certificate)
    {
        var data = Encoding.ASCII.GetBytes(digest);
        var bytes = new byte[signature.Length];
        if (!Convert.TryFromBase64String(signature, bytes, out var length))
        {
            return false;
        }

        try
        {
            return VerifyBytes(data, bytes.AsSpan(0, length), algorithm, certificate);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>The header value of the algorithm: <c>RS256</c>, <c>PS256</c> or <c>ES256</c>.</summary>
    public static string Name(IpsSignatureAlgorithm algorithm) => algorithm.ToString();

    /// <summary>Reads a header value, exactly as <see cref="Name"/> writes it.</summary>
    public static bool TryParse(string? value, out IpsSignatureAlgorithm algorithm)
    {
        foreach (var known in Enum.GetValues<IpsSignatureAlgorithm>())
        {
            if (string.Equals(value, Name(known), StringComparison.Ordinal))
            {
                algorithm = known;
                return true;
            }
        }

        algorithm = default;
        return false;
    }

    private static bool VerifyBytes(byte[] data, ReadOnlySpan<byte> signature, IpsSignatureAlgorithm algorithm, X509Certificate2 certificate)
    {
        if (algorithm == IpsSignatureAlgorithm.ES256)
        {
            using var ecdsa = certificate.GetECDsaPublicKey();
            return ecdsa is not null && ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }

        using var rsa = certificate.GetRSAPublicKey();
        return rsa is not null && rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, Padding(algorithm));
    }

    private static RSASignaturePadding Padding(IpsSignatureAlgorithm algorithm) =>
        algorithm == IpsSignatureAlgorithm.PS256 ? RSASignaturePadding.Pss : RSASignaturePadding.Pkcs1;

    private static InvalidOperationException MissingKey(X509Certificate2 certificate, IpsSignatureAlgorithm algorithm) =>
        new($"Certificate {certificate.Thumbprint} has no private key for {Name(algorithm)}.");
}
