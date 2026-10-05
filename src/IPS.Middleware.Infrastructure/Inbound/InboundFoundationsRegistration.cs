using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Registration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IPS.Middleware.Infrastructure.Inbound;

public static class InboundFoundationsRegistration
{
    // Registers foundations only. Persistence must also be registered; no hosted worker is started.
    public static IServiceCollection AddInboundFoundations(this IServiceCollection services, InboundSchedulingOptions? options = null)
    {
        if (options is not null)
        {
            services.AddSingleton(options);
        }
        else
        {
            services.TryAddSingleton(new InboundSchedulingOptions());
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new IncomingProcessingOptions());
        services.AddSingleton<InboundProcessingChannel>();
        services.AddSingleton<InboundReceiptRegistration>();
        services.AddSingleton<InboundWorkDiscovery>();
        services.AddSingleton<IncomingPaymentRegistration>();
        services.AddScoped<InboundReceiptIntake>();
        services.AddScoped<InboundWork>();
        services.AddScoped<IncomingPaymentIntake>();
        services.AddScoped<IncomingPaymentWork>();
        return services;
    }
}
