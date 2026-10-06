using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Transfers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IPS.Middleware.Infrastructure.Inbound.Workers;

public sealed class IncomingFollowUpWorker(
        IServiceScopeFactory scopes,
        IncomingWorkerOptions options,
        InboundSchedulingOptions scheduling,
        TimeProvider time,
        ILogger<IncomingFollowUpWorker> logger)
    : IncomingWorker(options, time, logger)
{
    protected override async Task RunAsync(CancellationToken stop, CancellationToken work)
    {
        var running = new Dictionary<Guid, Task>();
        try
        {
            await RefillAsync(token => DispatchDueAsync(running, token, work), scheduling.DiscoveryInterval, stop);
        }
        finally
        {
            await Task.WhenAll(running.Values);
        }
    }

    private async Task DispatchDueAsync(Dictionary<Guid, Task> running, CancellationToken stop, CancellationToken work)
    {
        foreach (var id in running.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToArray())
        {
            running.Remove(id);
        }

        if (running.Count == Options.CbsFollowUpCapacity)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var payments = await scope.ServiceProvider.GetRequiredService<IncomingReconciliation>().DiscoverAsync(stop);
        var transfers = await scope.ServiceProvider.GetRequiredService<IncomingTransferProcessing>().DiscoverAsync(stop);

        // Alternate between the two kinds so a backlog of one cannot starve the other.
        for (var index = 0; index < Math.Max(payments.Count, transfers.Count); index++)
        {
            if (index < payments.Count && !TryStart(running, payments[index], ProcessPaymentAsync, stop, work))
            {
                break;
            }

            if (index < transfers.Count && !TryStart(running, transfers[index], ProcessTransferAsync, stop, work))
            {
                break;
            }
        }
    }

    // False once capacity is used up or shutdown was requested.
    private bool TryStart(Dictionary<Guid, Task> running, Guid id, Func<Guid, CancellationToken, Task> process, CancellationToken stop, CancellationToken work)
    {
        if (running.Count == Options.CbsFollowUpCapacity || stop.IsCancellationRequested)
        {
            return false;
        }

        if (!running.ContainsKey(id))
        {
            running.Add(id, ObserveAsync(id, process, work));
        }

        return true;
    }

    private async Task ProcessPaymentAsync(Guid id, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IncomingReconciliation>().ProcessAsync(id, token);
    }

    private async Task ProcessTransferAsync(Guid id, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IncomingTransferProcessing>().ProcessAsync(id, token);
    }
}
