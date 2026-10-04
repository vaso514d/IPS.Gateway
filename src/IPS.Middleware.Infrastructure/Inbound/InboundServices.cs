using IPS.Middleware.Application.Inbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IPS.Middleware.Infrastructure.Inbound;

public static class InboundServices
{
    /// <summary>Registers foundations only. Persistence must also be registered; no hosted worker is started.</summary>
    public static IServiceCollection AddInboundFoundations(this IServiceCollection services, InboundSchedulingOptions? options = null)
    {
        services.AddSingleton(options ?? new InboundSchedulingOptions());
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<InboundProcessingChannel>();
        services.AddSingleton<InboundReceiptRegistration>();
        services.AddSingleton<InboundWorkDiscovery>();
        services.AddScoped<InboundReceiptIntake>();
        services.AddScoped<InboundWork>();
        return services;
    }
}
