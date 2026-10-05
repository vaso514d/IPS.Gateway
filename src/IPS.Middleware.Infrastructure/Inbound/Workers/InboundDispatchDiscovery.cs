using IPS.Middleware.Infrastructure.Repositories.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.Middleware.Infrastructure.Inbound.Workers;

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
        var db = scope.ServiceProvider.GetRequiredService<TransactionDbContext>();
        var ids = await db.InboundJournal.AsNoTracking().Where(InboundWorkRepository.DueAt(time.GetUtcNow()))
            .Where(row => db.IncomingReplies.Any(r => r.JournalId == row.Id) == replies)
            .OrderBy(row => row.NextActionAtUtc).ThenBy(row => row.ReceivedAtUtc).ThenBy(row => row.Id)
            .Take(options.DiscoveryBatch).Select(row => row.Id).ToListAsync(token);
        InboundJournalChannel channel = replies ? reply : processing;
        foreach (var id in ids)
        {
            channel.TryNotify(id);
        }
    }
}
