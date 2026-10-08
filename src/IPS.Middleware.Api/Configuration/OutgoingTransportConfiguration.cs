using IPS.Middleware.Infrastructure.Payments.Transport;

namespace IPS.Middleware.Api.Configuration;

internal static class OutgoingTransportConfiguration
{
    internal static IServiceCollection AddOutgoingTransportConfiguration(this IServiceCollection services)
    {
        services.AddSingleton(ReadSettings);
        services.AddOutgoingHttpClients();
        return services;
    }

    internal static void ValidateOutgoingTransport(this IServiceProvider services)
    {
        if (services.GetRequiredService<OutgoingTransportSettings>().Enabled)
        {
            services.ValidateOutgoingHttpClients();
        }
    }

    private static OutgoingTransportSettings ReadSettings(IServiceProvider services)
    {
        var settings = services.ReadSection<OutgoingTransportSettings>("Payments:Outgoing:Transport") ?? new OutgoingTransportSettings();
        settings.Validate(services.GetRequiredService<IHostEnvironment>().IsDevelopment());
        if (settings.Enabled)
        {
            services.RequireDatabase("Live outgoing transport");
        }

        return settings;
    }
}
