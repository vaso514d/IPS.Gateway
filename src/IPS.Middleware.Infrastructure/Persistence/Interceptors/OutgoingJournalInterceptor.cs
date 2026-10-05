using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Persistence.Interceptors;

internal sealed class OutgoingJournalInterceptor : SaveRuleInterceptor
{
    internal static readonly OutgoingJournalInterceptor Instance = new();

    protected override void Apply(TransactionDbContext db)
    {
        if (db.Phase == SavePhase.Events)
        {
            foreach (var (row, state) in db.PendingOutgoingMessages)
            {
                if (db.AuthorizedOutgoingMessages[row.Id] != PaymentJson.Write(row))
                    throw new InvalidOperationException("Journal evidence changed after parent validation.");
                db.Entry(row).State = state;
            }
            return;
        }
        if (db.Phase != SavePhase.Entities) return;
        foreach (var entry in db.ChangeTracker.Entries<OutgoingMessageRow>().ToArray())
        {
            if (entry.State == EntityState.Unchanged) continue;
            if (entry.State == EntityState.Modified)
            {
                var previous = entry.Property(p => p.Status).OriginalValue;
                var valid = entry.Entity.Direction == OutgoingMessageDirection.Outbound
                    ? previous == MessageJournalStatus.ReadyToSend && entry.Entity.Status == MessageJournalStatus.SendStarted
                    : previous == MessageJournalStatus.Received && entry.Entity.Status is MessageJournalStatus.Processed or MessageJournalStatus.Failed;
                if (!valid) throw new InvalidOperationException("Committed journal checkpoints cannot be rewritten.");
                string[] mutable = entry.Entity.Direction == OutgoingMessageDirection.Outbound
                    ? [nameof(OutgoingMessageRow.Status), nameof(OutgoingMessageRow.StartedAtUtc), nameof(OutgoingMessageRow.SubmissionOwner)]
                    : [nameof(OutgoingMessageRow.Status), nameof(OutgoingMessageRow.ProcessedAtUtc), nameof(OutgoingMessageRow.Failure), nameof(OutgoingMessageRow.MessageDefinition)];
                if (entry.Properties.Any(p => p.IsModified && !mutable.Contains(p.Metadata.Name)))
                    throw new InvalidOperationException("Journal evidence is immutable.");
            }
            if (entry.State == EntityState.Deleted ||
                !db.AuthorizedOutgoingMessages.TryGetValue(entry.Entity.Id, out var expected) || expected != PaymentJson.Write(entry.Entity) ||
                !db.ChangeTracker.Entries<OutgoingPayment>().Any(p => p.Entity.Id == entry.Entity.PaymentId && p.State == EntityState.Modified))
                throw new InvalidOperationException("Journal mutations require authorized immutable evidence and a version-checked payment.");
            // Parent UPDATE must win its rowversion check before a journal INSERT can encounter uniqueness.
            // Both phases are still inside the shared unit-of-work transaction.
            db.PendingOutgoingMessages.Add((entry.Entity, entry.State));
            entry.State = EntityState.Detached;
        }
    }
}
