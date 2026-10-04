using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Persistence.Interceptors;

internal sealed class InboundJournalInterceptor : SaveRuleInterceptor
{
    internal static readonly InboundJournalInterceptor Instance = new();
    private static readonly string[] Immutable = [nameof(InboundJournalEntry.ParticipantBic), nameof(InboundJournalEntry.Sequence),
        nameof(InboundJournalEntry.MessageType), nameof(InboundJournalEntry.RawXml), nameof(InboundJournalEntry.PossibleDuplicate),
        nameof(InboundJournalEntry.ReceivedAtUtc), nameof(InboundJournalEntry.HoldReason)];
    private static readonly string[] Work = [nameof(InboundJournalEntry.Status), nameof(InboundJournalEntry.NextActionAtUtc),
        nameof(InboundJournalEntry.ClaimToken), nameof(InboundJournalEntry.ClaimExpiresAtUtc)];

    protected override void Apply(TransactionDbContext db)
    {
        if (db.Phase != SavePhase.Entities) return;
        foreach (var entry in db.ChangeTracker.Entries<InboundJournalEntry>())
        {
            if (entry.State == EntityState.Deleted) throw new InvalidOperationException("Inbound receipts cannot be deleted.");
            if (entry.State != EntityState.Modified) continue;
            if (Immutable.Any(name => entry.Property(name).IsModified))
                throw new InvalidOperationException("Inbound receipt data is immutable.");
            if (Work.Any(name => entry.Property(name).IsModified) && !db.AuthorizedInboundWork.Contains(entry.Entity.Id))
                throw new InvalidOperationException("Inbound work changes require an ownership operation.");
        }
    }
}
