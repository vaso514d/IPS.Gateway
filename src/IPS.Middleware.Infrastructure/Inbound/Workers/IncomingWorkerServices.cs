using IPS.Middleware.Application.Inbound.Reconciliation;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.Middleware.Infrastructure.Inbound.Workers;

public static class IncomingWorkerServices
{
    // Persistence, protocol, clients and validated settings are supplied by the host.
    public static IServiceCollection AddIncomingWorkers(this IServiceCollection services)
    {
        services.AddIncomingComposition();
        services.AddSingleton<InboundDispatchDiscovery>();
        services.AddScoped<IncomingReconciliation>();
        services.AddHostedService<IncomingReceiveWorker>();
        services.AddHostedService<IncomingProcessingWorker>();
        services.AddHostedService<IncomingReplyWorker>();
        services.AddHostedService<IncomingFollowUpWorker>();
        return services;
    }
}
