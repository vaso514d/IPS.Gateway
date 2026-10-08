using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Replies;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Inbound.Workers;
using IPS.Middleware.Infrastructure.Persistence;

namespace IPS.Middleware.Api.Configuration;

internal static class IncomingWorkerConfiguration
{
    internal static IServiceCollection AddIncomingWorkerConfiguration(this IServiceCollection services)
    {
        services.AddSingleton(sp => sp.ReadSection<IncomingWorkerOptions>("Payments:Incoming:Workers") ?? new IncomingWorkerOptions());
        services.AddSingleton(sp => sp.ReadSection<Pacs008ProtocolProfile>("Payments:Incoming:Protocol")
            ?? throw new InvalidOperationException("Incoming workers require Payments:Incoming:Protocol:IpsBic."));
        services.AddPersistence(sp => sp.GetRequiredService<IConfiguration>().GetConnectionString(ConfigurationReading.ConnectionName)
            ?? throw new InvalidOperationException($"Incoming workers require ConnectionStrings:{ConfigurationReading.ConnectionName}."));
        services.AddOptions<HostOptions>()
            .Configure<IncomingWorkerOptions, IncomingProcessingOptions, IncomingReplyOptions, IncomingReconciliationOptions>(ExtendShutdownTimeout);
        services.AddIncomingWorkers();
        return services;
    }

    internal static void ValidateIncomingWorkers(this IServiceProvider services)
    {
        var options = services.GetRequiredService<IncomingWorkerOptions>();
        options.Validate(services.GetRequiredService<IncomingTransportSettings>());
        if (options.Enabled)
        {
            _ = services.GetRequiredService<Pacs008ProtocolProfile>();
        }
    }

    // Shutdown leaves room to drain the workers and persist evidence of their last attempts.
    private static void ExtendShutdownTimeout(
        HostOptions host,
        IncomingWorkerOptions workers,
        IncomingProcessingOptions processing,
        IncomingReplyOptions replies,
        IncomingReconciliationOptions reconciliation)
    {
        host.ServicesStopConcurrently = true;
        var persistence = new[] { processing.PersistenceBudget, replies.PersistenceBudget, reconciliation.PersistenceBudget }.Max();
        host.ShutdownTimeout = workers.ShutdownBudget + persistence;
    }
}
