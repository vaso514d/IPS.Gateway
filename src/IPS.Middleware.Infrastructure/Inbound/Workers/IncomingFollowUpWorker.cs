using IPS.Middleware.Application.Inbound.Reconciliation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IPS.Middleware.Infrastructure.Inbound.Workers;

public sealed class IncomingFollowUpWorker(IServiceScopeFactory scopes, IncomingWorkerOptions options,
    InboundSchedulingOptions scheduling, TimeProvider time, ILogger<IncomingFollowUpWorker> logger)
    : IncomingWorker(options, time, logger)
{
    protected override async Task RunAsync(CancellationToken stop, CancellationToken work)
    {
        var running = new Dictionary<Guid, Task>();
        try
        {
            await RefillAsync(async token =>
            {
                foreach (var id in running.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToArray()) running.Remove(id);
                if (running.Count == Options.CbsFollowUpCapacity) return;
                await using var scope = scopes.CreateAsyncScope();
                var ids = await scope.ServiceProvider.GetRequiredService<IncomingReconciliation>().DiscoverAsync(token);
                foreach (var id in ids)
                {
                    if (running.Count == Options.CbsFollowUpCapacity || stop.IsCancellationRequested) break;
                    if (!running.ContainsKey(id)) running.Add(id, ObserveAsync(id, ProcessAsync, work));
                }
            }, scheduling.DiscoveryInterval, stop);
        }
        finally { await Task.WhenAll(running.Values); }
    }

    private async Task ProcessAsync(Guid id, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IncomingReconciliation>().ProcessAsync(id, token);
    }
}
