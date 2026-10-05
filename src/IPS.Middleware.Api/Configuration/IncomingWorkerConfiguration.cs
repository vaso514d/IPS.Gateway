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
        services.AddSingleton(sp => sp.GetRequiredService<IConfiguration>()
            .GetSection("Payments:Incoming:Workers").Get<IncomingWorkerOptions>(o => o.ErrorOnUnknownConfiguration = true) ?? new());
        services.AddSingleton(sp => sp.GetRequiredService<IConfiguration>()
            .GetSection("Payments:Incoming:Protocol").Get<Pacs008ProtocolProfile>(o => o.ErrorOnUnknownConfiguration = true)
            ?? throw new InvalidOperationException("Incoming workers require Payments:Incoming:Protocol:IpsBic."));
        services.AddPersistence(sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("Middleware")
            ?? throw new InvalidOperationException("Incoming workers require ConnectionStrings:Middleware."));
        services.AddOptions<HostOptions>().Configure<IncomingWorkerOptions, IncomingProcessingOptions, IncomingReplyOptions, IncomingReconciliationOptions>(
            (host, workers, processing, replies, reconciliation) =>
            {
                host.ServicesStopConcurrently = true;
                var persistence = new[] { processing.PersistenceBudget, replies.PersistenceBudget, reconciliation.PersistenceBudget }.Max();
                host.ShutdownTimeout = workers.ShutdownBudget + persistence;
            });
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
}
