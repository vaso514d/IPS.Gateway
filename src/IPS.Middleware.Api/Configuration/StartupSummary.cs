using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Execution;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Inbound.Workers;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Payments.Transport;
using IPS.Middleware.Infrastructure.Proxy;
using IPS.Middleware.Infrastructure.Transport;
using IPS.MiidleWear.Security;
using IPS.MiidleWear.Security.AspNetCore;
using Microsoft.Extensions.Options;

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

    // Specification 021: open is allowed, so it is reported as a warning rather than refused. Loading the options here also
    // loads the trusted certificates, so a wrong one stops startup before any request is served.
    private static void LogCoreAuthentication(IServiceProvider services, ILogger logger, IncomingTransportSettings incoming, OutgoingTransportSettings outgoing)
    {
        var server = services.GetRequiredService<IOptionsMonitor<IpsServerOptions>>().Get(IpsServerAuthentication.Scheme);
        if (server.ApiTokens.Count == 0 && server.Verifier is null)
        {
            logger.LogWarning("Server authentication: [RequireIpsAuthentication] endpoints are open (no Server:ApiTokens or Server:Signing:TrustedCertificates)");
        }
        else
        {
            logger.LogInformation("Server authentication: [RequireIpsAuthentication] endpoints require {Token} and {Signature}",
                server.ApiTokens.Count == 0 ? "no API token" : $"one of {server.ApiTokens.Count} API token(s)",
                server.Verifier is null ? "no signature" : $"a signature by one of {server.TrustedCertificates} trusted certificate(s)");
        }

        logger.LogInformation("Calls to the CBS: API token incoming {IncomingToken}, outgoing {OutgoingToken}; signing incoming {IncomingSigning}, outgoing {OutgoingSigning}",
            Configured(incoming.Cbs.ApiToken), Configured(outgoing.Cbs.ApiToken), Configured(incoming.Cbs.Signing), Configured(outgoing.Cbs.Signing));
    }

    private static string Configured(string? secret) => string.IsNullOrWhiteSpace(secret) ? "none" : "configured";

    private static string Configured(IpsSigningSettings? signing) => signing is { IsConfigured: true } ? signing.Algorithm : "none";

    private static string OnOff(bool enabled) => enabled ? "on" : "off";

    private static string Url(HttpEndpointSettings endpoint) => string.IsNullOrWhiteSpace(endpoint.BaseUrl) ? "(no URL)" : endpoint.BaseUrl;

    private static string Configured(CertificateSettings? certificate) => certificate is null ? "none" : "configured";
}
