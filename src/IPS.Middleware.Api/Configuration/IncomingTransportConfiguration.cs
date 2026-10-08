using IPS.Middleware.Infrastructure.Inbound.Transport;

namespace IPS.Middleware.Api.Configuration;

internal static class IncomingTransportConfiguration
{
    internal static IServiceCollection AddIncomingTransportConfiguration(this IServiceCollection services)
    {
        services.AddSingleton(ReadSettings);
        services.AddIncomingHttpClients();
        return services;
    }

    internal static void ValidateIncomingTransport(this IServiceProvider services)
    {
        if (!services.GetRequiredService<IncomingTransportSettings>().Enabled)
        {
            return;
        }

        _ = services.GetRequiredService<IncomingTransportCertificates>();
        services.ValidateIncomingHttpClients();
    }

    private static IncomingTransportSettings ReadSettings(IServiceProvider services)
    {
        var settings = services.ReadSection<IncomingTransportSettings>("Payments:Incoming:Transport") ?? new IncomingTransportSettings();
        settings.Validate(services.GetRequiredService<IHostEnvironment>().IsDevelopment());
        if (settings.Enabled)
        {
            services.RequireDatabase("Live incoming transport");
        }

        return settings;
    }
}
