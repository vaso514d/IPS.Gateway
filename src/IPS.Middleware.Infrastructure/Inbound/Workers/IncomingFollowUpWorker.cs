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
    private readonly FollowUpAdmission _admission = new();

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
        foreach (var due in _admission.Select(payments, transfers, Options.CbsFollowUpCapacity - running.Count, running.ContainsKey))
        {
            if (stop.IsCancellationRequested)
            {
                break;
            }

            running.Add(due.Id, ObserveAsync(due.Id, due.Kind == FollowUpKind.Transfer ? ProcessTransferAsync : ProcessPaymentAsync, work));
        }
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
