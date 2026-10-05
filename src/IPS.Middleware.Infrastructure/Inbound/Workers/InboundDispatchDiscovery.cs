using IPS.Middleware.Application.Inbound.Receipts;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.Middleware.Infrastructure.Inbound.Workers;

// Queues due receipts for this process; ownership is still acquired per receipt by the worker that runs it.
public sealed class InboundDispatchDiscovery(
    IServiceScopeFactory scopes,
    InboundProcessingChannel processing,
    InboundReplyChannel reply,
    InboundSchedulingOptions options,
    TimeProvider time)
{
    public async Task RefillAsync(bool replies, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IInboundWorkRepository>();
        var due = await repository.FindDueAsync(time.GetUtcNow(), options.DiscoveryBatch, replies, token);
        InboundJournalChannel channel = replies ? reply : processing;
        foreach (var journalId in due)
        {
            channel.TryNotify(journalId);
        }
    }
}
