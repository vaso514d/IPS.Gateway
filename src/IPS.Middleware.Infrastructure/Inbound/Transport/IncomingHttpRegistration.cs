using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Replies;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.Middleware.Infrastructure.Inbound.Transport;

public static class IncomingHttpRegistration
{
    internal const string IpsReceive = "incoming-ips-receive";
    internal const string IpsReply = "incoming-ips-reply";
    internal const string Cbs = "incoming-cbs";

    public static IServiceCollection AddIncomingHttpClients(this IServiceCollection services)
    {
        services.AddSingleton<IncomingTransportCertificates>();
        services.AddSingleton<ISigningCertificateSource>(sp => sp.GetRequiredService<IncomingTransportCertificates>());
        services.AddTransient<IncomingIpsClient>();
        services.AddTransient<IIncomingReceiveClient>(sp => sp.GetRequiredService<IncomingIpsClient>());
        services.AddTransient<IIncomingReplyClient>(sp => sp.GetRequiredService<IncomingIpsClient>());
        services.AddTransient<IncomingCbsClient>();
        services.AddTransient<IIncomingCoreClient>(sp => sp.GetRequiredService<IncomingCbsClient>());
        services.AddTransient<IIncomingReversalClient>(sp => sp.GetRequiredService<IncomingCbsClient>());
        services.AddTransient<IIncomingReplyProtocol>(sp => new IncomingReplyProtocol(
            sp.GetRequiredService<Pacs008MessageSigner>(), sp.GetRequiredService<ISigningCertificateSource>(),
            sp.GetRequiredService<IncomingTransportCertificates>().IpsSignatureTrust));
        AddClient(services, IpsReceive, ips: true, receive: true);
        AddClient(services, IpsReply, ips: true, receive: false);
        AddClient(services, Cbs, ips: false, receive: false);
        return services;
    }

    public static void ValidateIncomingHttpClients(this IServiceProvider services)
    {
        var factory = services.GetRequiredService<IHttpClientFactory>();
        foreach (var name in new[] { IpsReceive, IpsReply, Cbs })
        {
            using var client = factory.CreateClient(name);
        }
    }

    internal static void RequireParticipant(IncomingTransportSettings settings, string participant)
    {
        if (!settings.Enabled)
        {
            throw new InvalidOperationException("Incoming live transport is disabled.");
        }

        if (!string.Equals(participant, settings.ParticipantBic, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Payment participant does not match the configured transport participant.");
        }
    }

    private static void AddClient(IServiceCollection services, string name, bool ips, bool receive)
    {
        SingleAttemptHttp.Add(services, name, sp =>
        {
            var settings = sp.GetRequiredService<IncomingTransportSettings>();
            if (!settings.Enabled)
            {
                throw new InvalidOperationException("Incoming live transport is disabled.");
            }

            var endpoint = ips ? settings.Ips : settings.Cbs;
            return new(endpoint, receive ? 1 : ips ? endpoint.ConnectionLimit - 1 : endpoint.ConnectionLimit,
                receive ? settings.ReceiveTimeout : endpoint.RequestTimeout,
                sp.GetRequiredService<IncomingTransportCertificates>().Tls(ips, endpoint.CheckCertificateRevocation));
        });
    }
}
