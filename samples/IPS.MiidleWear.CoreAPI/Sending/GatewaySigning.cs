using System.Security.Cryptography.X509Certificates;
using IPS.MiidleWear.Security;
using Microsoft.Extensions.Options;

namespace IPS.MiidleWear.CoreAPI.Sending;

// Signs what CoreAPI sends to the gateway with IPS.MiidleWear.Security, as a core system does. The certificate is loaded
// once, when the first request is sent; without one the requests go unsigned and a gateway that requires signatures
// answers 401.
public sealed class GatewaySigning(IOptions<GatewayOptions> options, ILogger<GatewaySigning> logger)
{
    private readonly Lazy<(X509Certificate2 Certificate, IpsSignatureAlgorithm Algorithm)?> _signer = new(() => Load(options.Value.Signing, logger));

    public DelegatingHandler CreateHandler() =>
        _signer.Value is { } signer ? new IpsSigningHandler(signer.Certificate, signer.Algorithm) : new UnsignedHandler();

    private static (X509Certificate2, IpsSignatureAlgorithm)? Load(GatewaySigningOptions signing, ILogger logger)
    {
        if (!signing.IsConfigured)
        {
            logger.LogWarning("Requests to the gateway are not signed (no Gateway:Signing certificate)");
            return null;
        }

        if (!IpsContentSignature.TryParse(signing.Algorithm, out var algorithm))
        {
            throw new InvalidOperationException($"Gateway:Signing:Algorithm must be RS256, PS256 or ES256, not '{signing.Algorithm}'.");
        }

        var certificate = string.IsNullOrWhiteSpace(signing.Thumbprint)
            ? IpsCertificates.FromFile(signing.Path!, signing.Password)
            : IpsCertificates.FromStore(signing.Thumbprint);
        logger.LogInformation("Requests to the gateway are signed with {Algorithm} by certificate {Thumbprint} ({Subject})",
            signing.Algorithm, certificate.Thumbprint, certificate.Subject);
        return (certificate, algorithm);
    }

    private sealed class UnsignedHandler : DelegatingHandler;
}
