using IPS.Middleware.Application.Proxy;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.Middleware.Infrastructure.Proxy;

public static class ProxyHttpRegistration
{
    internal const string Name = "proxy";

    public static IServiceCollection AddProxyClient(this IServiceCollection services)
    {
        services.AddSingleton<ProxyTransportCertificates>();
        services.AddTransient<IProxyClient, ProxyClient>();
        // Built by hand so the Proxy signs with its own optional certificate, not the outgoing IPS one.
        services.AddTransient<IProxyProtocol>(sp => new ProxyProtocol(
            sp.GetRequiredService<ProxySettings>(),
            sp.GetRequiredService<Pacs008MessageSigner>(),
            sp.GetRequiredService<ProxyTransportCertificates>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddTransient<ProxyManagement>();
        SingleAttemptHttp.Add(services, Name, sp =>
        {
            var settings = sp.GetRequiredService<ProxySettings>();
            if (!settings.Enabled)
            {
                throw new InvalidOperationException("Proxy management is disabled.");
            }

            return new(settings.Endpoint, settings.Endpoint.ConnectionLimit, settings.Endpoint.RequestTimeout,
                sp.GetRequiredService<ProxyTransportCertificates>().Tls(settings.Endpoint.CheckCertificateRevocation));
        });
        return services;
    }

    public static void ValidateProxyClient(this IServiceProvider services)
    {
        _ = services.GetRequiredService<ProxyTransportCertificates>();
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(Name);
    }
}
