using System.Security.Cryptography.X509Certificates;

namespace IPS.MiidleWear.Security;

/// <summary>
/// A certificate in configuration, from exactly one source: <see cref="Thumbprint"/> (the Windows stores),
/// <see cref="Path"/> (a PFX with <see cref="Password"/>, or a public .cer/.pem) or <see cref="Pem"/> (a public
/// certificate as text, as in a container).
/// </summary>
public class IpsCertificateSettings
{
    /// <summary>The certificate's thumbprint, looked up in CurrentUser and then LocalMachine.</summary>
    public string? Thumbprint { get; set; }

    /// <summary>A certificate file.</summary>
    public string? Path { get; set; }

    /// <summary>The password of a PFX file.</summary>
    public string? Password { get; set; }

    /// <summary>A public certificate in PEM text.</summary>
    public string? Pem { get; set; }

    /// <summary>Whether a source is given; an empty entry stands for no certificate.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Thumbprint) || !string.IsNullOrWhiteSpace(Path) || !string.IsNullOrWhiteSpace(Pem);

    /// <summary>Loads the certificate.</summary>
    /// <exception cref="InvalidOperationException">Not exactly one source is given, or it cannot be found.</exception>
    public X509Certificate2 Load()
    {
        string?[] sources = [Thumbprint, Path, Pem];
        if (sources.Count(source => !string.IsNullOrWhiteSpace(source)) != 1)
        {
            throw new InvalidOperationException("Give exactly one certificate source: Thumbprint, Path or Pem.");
        }

        if (!string.IsNullOrWhiteSpace(Thumbprint))
        {
            return IpsCertificates.FromStore(Thumbprint);
        }

        return string.IsNullOrWhiteSpace(Path)
            ? X509Certificate2.CreateFromPem(Pem)
            : IpsCertificates.FromFile(Path, Password);
    }
}

/// <summary>
/// The certificate a client signs its requests with, which needs its private key, and the signature algorithm:
/// <c>RS256</c> or <c>PS256</c> for an RSA key, <c>ES256</c> for an ECDSA key.
/// </summary>
public sealed class IpsSigningSettings : IpsCertificateSettings
{
    /// <summary><c>RS256</c> (the default), <c>PS256</c> or <c>ES256</c>.</summary>
    public string Algorithm { get; set; } = nameof(IpsSignatureAlgorithm.RS256);

    /// <summary>The configured algorithm.</summary>
    /// <exception cref="InvalidOperationException">The name is not RS256, PS256 or ES256.</exception>
    public IpsSignatureAlgorithm SignatureAlgorithm() =>
        IpsContentSignature.TryParse(Algorithm?.Trim(), out var algorithm)
            ? algorithm
            : throw new InvalidOperationException($"Signing:Algorithm must be RS256, PS256 or ES256, not '{Algorithm}'.");
}
