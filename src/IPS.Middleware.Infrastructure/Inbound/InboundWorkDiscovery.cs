using IPS.Middleware.Application.Inbound.Receipts;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.Middleware.Infrastructure.Inbound;

public sealed class InboundWorkDiscovery(
        IServiceScopeFactory scopes,
        InboundProcessingChannel channel,
        InboundSchedulingOptions options,
        TimeProvider timeProvider)
{
    // Queues one batch of due IDs from SQL; ownership is still acquired per ID by the processor.
    public async Task<int> RefillAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var due = await scope.ServiceProvider.GetRequiredService<IInboundWorkRepository>()
            .FindDueAsync(timeProvider.GetUtcNow(), options.DiscoveryBatch, cancellationToken);
        return due.Count(channel.TryNotify);
    }
}
