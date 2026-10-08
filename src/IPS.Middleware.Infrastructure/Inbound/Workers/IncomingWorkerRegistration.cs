using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IPS.Middleware.Infrastructure.Inbound.Workers;

public static class IncomingWorkerRegistration
{
    // Persistence, protocol, clients and validated settings are supplied by the host.
    public static IServiceCollection AddIncomingWorkers(this IServiceCollection services)
    {
        services.AddIncomingComposition();
        // Replies leave the acknowledgement connections free, replacing composition's default that reserves only receive.
        // Validation refuses enabled workers without reply capacity; disabled workers still construct the admission.
        services.Replace(ServiceDescriptor.Singleton(sp => new IncomingReplyAdmission(Math.Max(1, sp.GetRequiredService<IncomingWorkerOptions>()
            .IpsSendCapacity(sp.GetRequiredService<IncomingTransportSettings>())))));
        services.AddSingleton<InboundDispatchDiscovery>();
        services.AddScoped<IncomingReconciliation>();
        services.AddHostedService<IncomingReceiveWorker>();
        services.AddHostedService<IncomingProcessingWorker>();
        services.AddHostedService<IncomingReplyWorker>();
        services.AddHostedService<IncomingFollowUpWorker>();
        return services;
    }
}
