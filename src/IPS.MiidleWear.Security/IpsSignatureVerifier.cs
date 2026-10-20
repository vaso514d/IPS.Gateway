using System.Security.Cryptography.X509Certificates;

namespace IPS.MiidleWear.Security;

/// <summary>The outcome of checking a signed request; only <see cref="Valid"/> accepts it.</summary>
public enum IpsSignatureCheck
{
    /// <summary>The signature is valid.</summary>
    Valid,

    /// <summary>A signature header is missing or empty.</summary>
    MissingHeader,

    /// <summary>The algorithm is unknown or not allowed.</summary>
    AlgorithmNotAllowed,

    /// <summary>The digest header does not equal the SHA-256 of the body received.</summary>
    DigestMismatch,

    /// <summary>The thumbprint names no trusted certificate.</summary>
    UnknownCertificate,

    /// <summary>The trusted certificate is not valid at this time.</summary>
    CertificateNotValidNow,

    /// <summary>The signature does not verify with the certificate's public key.</summary>
    InvalidSignature
}

/// <summary>
/// The four header values of a signed request, as received; any can be <c>null</c> when absent.
/// </summary>
public sealed record IpsSignedRequest(string? ContentSha256, string? Signature, string? SignatureAlgorithm, string? Certificate);

/// <summary>
/// Checks signed requests against trusted certificates (public keys only), found by their thumbprint. More than one
/// certificate can be trusted at once, so a certificate can be replaced without downtime.
/// </summary>
public sealed class IpsSignatureVerifier
{
    private readonly Dictionary<string, X509Certificate2> _trusted;
    private readonly HashSet<IpsSignatureAlgorithm> _allowed;
    private readonly TimeProvider _timeProvider;

    /// <summary>Trusts <paramref name="certificates"/> and accepts <paramref name="algorithms"/>.</summary>
    public IpsSignatureVerifier(IEnumerable<X509Certificate2> certificates, IEnumerable<IpsSignatureAlgorithm> algorithms, TimeProvider timeProvider)
    {
        _trusted = certificates.ToDictionary(certificate => certificate.Thumbprint, StringComparer.OrdinalIgnoreCase);
        _allowed = [.. algorithms];
        _timeProvider = timeProvider;
        if (_trusted.Count == 0 || _allowed.Count == 0)
        {
            throw new ArgumentException("At least one trusted certificate and one algorithm are required.");
        }
    }

    /// <summary>Checks the request's headers against the body bytes received.</summary>
    public IpsSignatureCheck Verify(IpsSignedRequest request, ReadOnlySpan<byte> body)
    {
        if (IsMissing(request))
        {
            return IpsSignatureCheck.MissingHeader;
        }

        if (!IpsContentSignature.TryParse(request.SignatureAlgorithm, out var algorithm) || !_allowed.Contains(algorithm))
        {
            return IpsSignatureCheck.AlgorithmNotAllowed;
        }

        if (!string.Equals(request.ContentSha256, IpsContentSignature.Digest(body), StringComparison.Ordinal))
        {
            return IpsSignatureCheck.DigestMismatch;
        }

        if (!_trusted.TryGetValue(request.Certificate!, out var certificate))
        {
            return IpsSignatureCheck.UnknownCertificate;
        }

        if (!IsValidNow(certificate))
        {
            return IpsSignatureCheck.CertificateNotValidNow;
        }

        return IpsContentSignature.Verify(request.ContentSha256!, request.Signature!, algorithm, certificate)
            ? IpsSignatureCheck.Valid
            : IpsSignatureCheck.InvalidSignature;
    }

    private static bool IsMissing(IpsSignedRequest request) =>
        string.IsNullOrWhiteSpace(request.ContentSha256) ||
        string.IsNullOrWhiteSpace(request.Signature) ||
        string.IsNullOrWhiteSpace(request.SignatureAlgorithm) ||
        string.IsNullOrWhiteSpace(request.Certificate);

    private bool IsValidNow(X509Certificate2 certificate)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        return certificate.NotBefore.ToUniversalTime() <= now && now <= certificate.NotAfter.ToUniversalTime();
    }
}
