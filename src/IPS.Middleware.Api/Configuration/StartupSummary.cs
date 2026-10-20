using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Execution;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Inbound.Workers;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Payments.Transport;
using IPS.Middleware.Infrastructure.Proxy;
using IPS.Middleware.Infrastructure.Transport;

namespace IPS.Middleware.Api.Configuration;

// One startup line per feature with what is on and where it connects, so an operator can tell a host that runs nothing
// from one that cannot reach IPS. Logs no secret: certificates are reported as configured or not.
internal static class StartupSummary
{
    internal static void LogMiddlewareSettings(this IServiceProvider services, ILogger logger)
    {
        var incoming = services.GetRequiredService<IncomingTransportSettings>();
        var workers = services.GetRequiredService<IncomingWorkerOptions>();
        var outgoing = services.GetRequiredService<OutgoingTransportSettings>();
        var execution = services.GetRequiredService<OutgoingExecutionOptions>();
        var proxy = services.GetRequiredService<ProxySettings>();
        var signing = services.GetRequiredService<Pacs008SigningPolicy>();

        if (incoming.Enabled || outgoing.Enabled || proxy.Enabled)
        {
            var bank = services.GetRequiredService<BankIdentity>();
            logger.LogInformation("Bank {Bic} (IPS channel header), NBG {Nbg}", bank.Bic, bank.NbgBic);
        }

        logger.LogInformation(
            "Incoming: transport {Transport}, workers {Workers}; IPS {IpsUrl} (client certificate {IpsCertificate}, {Trust} IPS signature certificate(s)); CBS {CbsUrl}",
            OnOff(incoming.Enabled), OnOff(workers.Enabled), Url(incoming.Ips), Configured(incoming.Ips.ClientCertificate),
            incoming.IpsSignatureTrust.Length, Url(incoming.Cbs));
        logger.LogInformation(
            "Outgoing: transport {Transport}, execution {Execution}; IPS {IpsUrl} (client certificate {IpsCertificate}, {Trust} IPS signature certificate(s)); CBS {CbsUrl}",
            OnOff(outgoing.Enabled), OnOff(execution.Enabled), Url(outgoing.Ips), Configured(outgoing.Ips.ClientCertificate),
            outgoing.IpsSignatureTrust.Length, Url(outgoing.Cbs));
        logger.LogInformation("Proxy: {Proxy}; {ProxyUrl}", OnOff(proxy.Enabled), Url(proxy.Endpoint));
        LogCoreAuthentication(services, logger, incoming, outgoing);
        logger.LogInformation("Signing: unsigned messages without a certificate {Unsigned}", signing.AllowUnsignedWithoutCertificate ? "allowed (Development)" : "refused");
        if (incoming.Enabled && !workers.Enabled)
        {
            logger.LogWarning("Incoming transport is on but the workers are off: nothing polls IPS. Set Payments:Incoming:Workers:Enabled=true to receive.");
        }
    }

    // Specification 021: open is allowed, so it is reported as a warning rather than refused.
    private static void LogCoreAuthentication(IServiceProvider services, ILogger logger, IncomingTransportSettings incoming, OutgoingTransportSettings outgoing)
    {
        var signing = services.GetRequiredService<CoreRequestSigning>();
        if (signing.Verifier is null)
        {
            logger.LogWarning("Core authentication: [RequireCoreSignature] endpoints are not checked (no CoreAuthentication:TrustedCertificates)");
        }
        else
        {
            logger.LogInformation("Core authentication: [RequireCoreSignature] endpoints must be signed by one of {Count} trusted certificate(s)", signing.TrustedCertificates);
        }

        logger.LogInformation("Core authentication: API token to the CBS incoming {Incoming}, outgoing {Outgoing}",
            Configured(incoming.Cbs.ApiToken), Configured(outgoing.Cbs.ApiToken));
    }

    private static string Configured(string? secret) => string.IsNullOrWhiteSpace(secret) ? "none" : "configured";

    private static string OnOff(bool enabled) => enabled ? "on" : "off";

    private static string Url(HttpEndpointSettings endpoint) => string.IsNullOrWhiteSpace(endpoint.BaseUrl) ? "(no URL)" : endpoint.BaseUrl;

    private static string Configured(CertificateSettings? certificate) => certificate is null ? "none" : "configured";
}
