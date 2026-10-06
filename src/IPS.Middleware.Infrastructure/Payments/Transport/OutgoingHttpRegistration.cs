using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Investigation;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Infrastructure.Payments.Investigation;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Payments.Pacs009;
using IPS.Middleware.Infrastructure.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.Middleware.Infrastructure.Payments.Transport;

public static class OutgoingHttpRegistration
{
    internal const string Ips = "outgoing-ips";
    internal const string Cbs = "outgoing-cbs-status";

    public static IServiceCollection AddOutgoingHttpClients(this IServiceCollection services)
    {
        services.AddSingleton<OutgoingTransportCertificates>();
        services.AddTransient<IIpsTransport, OutgoingIpsClient>();
        services.AddTransient<IOutgoingStatusReceiver, OutgoingStatusClient>();
        services.AddTransient<IOutgoingMessageProtocol>(sp => new Pacs008Preparation(
            sp.GetRequiredService<Pacs008MessageSigner>(), sp.GetRequiredService<OutgoingTransportCertificates>()));
        services.AddTransient<IOutgoingMessageProtocol>(sp => new Pacs009Preparation(
            sp.GetRequiredService<Pacs008MessageSigner>(), sp.GetRequiredService<OutgoingTransportCertificates>()));
        services.AddTransient<IIpsReplyInterpreter>(sp => new IpsReplyInterpreter(sp.GetRequiredService<OutgoingTransportCertificates>().IpsSignatureTrust));
        services.AddTransient<IInvestigationProtocol>(sp => new InvestigationProtocol(
            sp.GetRequiredService<Pacs008MessageSigner>(),
            sp.GetRequiredService<OutgoingTransportCertificates>(),
            sp.GetRequiredService<Pacs008SigningPolicy>(),
            new Pacs028ReplyInterpreter(sp.GetRequiredService<OutgoingTransportCertificates>().IpsSignatureTrust)));
        Add(services, Ips, true);
        Add(services, Cbs, false);
        return services;
    }

    public static void ValidateOutgoingHttpClients(this IServiceProvider services)
    {
        _ = services.GetRequiredService<OutgoingTransportCertificates>();
        foreach (var name in new[] { Ips, Cbs })
        {
            using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(name);
        }
    }

    private static void Add(IServiceCollection services, string name, bool ips) => SingleAttemptHttp.Add(services, name, sp =>
    {
        var settings = sp.GetRequiredService<OutgoingTransportSettings>();
        if (!settings.Enabled)
        {
            throw new InvalidOperationException("Outgoing live transport is disabled.");
        }

        var endpoint = ips ? settings.Ips : settings.Cbs;
        return new(endpoint, endpoint.ConnectionLimit, endpoint.RequestTimeout,
            sp.GetRequiredService<OutgoingTransportCertificates>().Tls(ips, endpoint.CheckCertificateRevocation));
    });
}
