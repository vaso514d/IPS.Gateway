using IPS.Middleware.Application.Diagnostics;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Diagnostics;

// One cheap read of what is waiting: due work by kind with the age of the oldest, and the inbound journal by status.
internal sealed class BacklogReader(TransactionDbContext db, TimeProvider time)
{
    internal async Task<BacklogSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var items = new List<BacklogItem>
        {
            await DueAsync("outgoing_payments", db.OutgoingMetadata.AsNoTracking().Select(row => row.NextActionAtUtc), now, cancellationToken),
            await DueAsync("incoming_payments", db.IncomingMetadata.AsNoTracking().Select(row => row.NextActionAtUtc), now, cancellationToken),
            await DueAsync("incoming_transfers", db.IncomingTransferMetadata.AsNoTracking().Select(row => row.NextActionAtUtc), now, cancellationToken),
            await DueAsync("inbound_receipts", db.InboundJournal.AsNoTracking()
                .Where(row => row.Status == InboundProcessingStatus.Pending)
                .Select(row => row.NextActionAtUtc), now, cancellationToken),
            await DueAsync("callbacks", db.OutgoingStatusDeliveries.AsNoTracking()
                .Where(row => row.State == StatusDeliveryState.Pending)
                .Select(row => row.NextAtUtc), now, cancellationToken)
        };
        var journal = await db.InboundJournal.AsNoTracking()
            .GroupBy(row => row.Status)
            .Select(group => new { Status = group.Key, Count = group.LongCount() })
            .ToArrayAsync(cancellationToken);
        return new BacklogSnapshot(
            now,
            items,
            journal.Select(entry => new JournalCount(entry.Status.ToString().ToLowerInvariant(), entry.Count)).ToArray());
    }

    private static async Task<BacklogItem> DueAsync(string kind, IQueryable<DateTimeOffset?> nextActions, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = nextActions.Where(next => next != null && next <= now);
        var count = await due.LongCountAsync(cancellationToken);
        var oldest = count == 0 ? null : await due.MinAsync(cancellationToken);
        return new BacklogItem(kind, count, oldest is { } at ? Math.Max(0, (now - at).TotalSeconds) : 0);
    }
}
