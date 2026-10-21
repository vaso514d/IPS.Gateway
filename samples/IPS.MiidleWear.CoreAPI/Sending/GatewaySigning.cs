using System.Security.Cryptography.X509Certificates;
using IPS.MiidleWear.Security;
using Microsoft.Extensions.Options;

namespace IPS.MiidleWear.CoreAPI.Sending;

// What CoreAPI sends with its requests to the gateway, with IPS.MiidleWear.Security, as a core system does: the API token
// and the body signature, each as configured under Gateway. The certificate is loaded once, with the first request; each
// HTTP handler pipeline gets new handlers.
public sealed class GatewaySigning(IOptions<GatewayOptions> options, ILogger<GatewaySigning> logger)
{
    private readonly Lazy<X509Certificate2?> _certificate = new(() => Load(options.Value.Signing, logger));

    public DelegatingHandler CreateTokenHandler() => new IpsApiTokenHandler(options.Value.ApiToken);

    public DelegatingHandler CreateSigningHandler() =>
        _certificate.Value is { } certificate
            ? new IpsSigningHandler(certificate, options.Value.Signing.SignatureAlgorithm())
            : IpsSigningHandler.Create(signing: null);

    private static X509Certificate2? Load(IpsSigningSettings signing, ILogger logger)
    {
        if (!signing.IsConfigured)
        {
            logger.LogWarning("Requests to the gateway are not signed (no Gateway:Signing certificate)");
            return null;
        }

        var certificate = signing.Load();
        logger.LogInformation("Requests to the gateway are signed with {Algorithm} by certificate {Thumbprint} ({Subject})",
            signing.SignatureAlgorithm(), certificate.Thumbprint, certificate.Subject);
        return certificate;
    }
}
