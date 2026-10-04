using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Persistence.Interceptors;

/// <summary>Receipt and incoming payment data never change; only ownership operations change their work fields.</summary>
internal sealed class InboundPersistenceInterceptor : SaveRuleInterceptor
{
    internal static readonly InboundPersistenceInterceptor Instance = new();

    private static readonly Rules Receipt = new("Inbound receipt",
        Immutable: [nameof(InboundJournalEntry.ParticipantBic), nameof(InboundJournalEntry.Sequence), nameof(InboundJournalEntry.MessageType),
            nameof(InboundJournalEntry.RawXml), nameof(InboundJournalEntry.PossibleDuplicate), nameof(InboundJournalEntry.ReceivedAtUtc)],
        WriteOnce: [nameof(InboundJournalEntry.HoldReason), nameof(InboundJournalEntry.IncomingPaymentId), nameof(InboundJournalEntry.OriginalJson)],
        Owned: [nameof(InboundJournalEntry.Status), nameof(InboundJournalEntry.NextActionAtUtc),
            nameof(InboundJournalEntry.ClaimToken), nameof(InboundJournalEntry.ClaimExpiresAtUtc)]);

    private static readonly Rules Payment = new("Incoming payment",
        Immutable: [nameof(IncomingPayment.ParticipantBic), nameof(IncomingPayment.EndToEndId), nameof(IncomingPayment.RegisteredAtUtc), RequestJson, IncomingProcessingColumns.ContextJson],
        WriteOnce: [],
        Owned: [ClaimToken, ClaimExpiresAtUtc, NextActionAtUtc]);

    protected override void Apply(TransactionDbContext db)
    {
        if (db.Phase != SavePhase.Entities) return;
        foreach (var entry in db.ChangeTracker.Entries<InboundJournalEntry>()) Receipt.Check(entry, entry.Entity.Id, db.AuthorizedInboundWork);
        foreach (var entry in db.ChangeTracker.Entries<IncomingPayment>())
        {
            Payment.Check(entry, entry.Entity.Id, db.AuthorizedIncomingPaymentWork);
            if (entry.State == EntityState.Modified && !db.AuthorizedIncomingProcessing.Contains(entry.Entity.Id) &&
                entry.Properties.Any(p => p.IsModified && (!p.Metadata.IsShadowProperty() ||
                    p.Metadata.Name is IncomingProcessingColumns.CheckpointVersion or IncomingProcessingColumns.FollowUpAtUtc)))
                throw new InvalidOperationException("Incoming processing changes require payment ownership.");
        }
        foreach (var call in db.ChangeTracker.Entries<IncomingCoreCallRow>())
        {
            if (call.State == EntityState.Deleted) throw new InvalidOperationException("CBS call evidence cannot be deleted.");
            if (call.State is not (EntityState.Added or EntityState.Modified)) continue;
            if (!db.AuthorizedIncomingCalls.Contains(call.Entity.Id) || !db.AuthorizedIncomingProcessing.Contains(call.Entity.PaymentId))
                throw new InvalidOperationException("CBS call evidence requires a fenced payment checkpoint.");
            if (call.State == EntityState.Modified && call.Properties.Any(p => p.IsModified &&
                (p.Metadata.Name is not (nameof(IncomingCoreCallRow.CompletionJson) or nameof(IncomingCoreCallRow.Consumed)) ||
                 p.Metadata.Name == nameof(IncomingCoreCallRow.CompletionJson) && p.OriginalValue is not null ||
                 p.Metadata.Name == nameof(IncomingCoreCallRow.Consumed) && Equals(p.OriginalValue, true))))
                throw new InvalidOperationException("CBS call evidence is write-once.");
        }
    }

    private sealed record Rules(string Name, string[] Immutable, string[] WriteOnce, string[] Owned)
    {
        public void Check(EntityEntry entry, Guid id, HashSet<Guid> authorized)
        {
            if (entry.State == EntityState.Deleted) throw new InvalidOperationException($"{Name} rows cannot be deleted.");
            if (entry.State != EntityState.Modified) return;
            if (Immutable.Any(name => entry.Property(name).IsModified) ||
                WriteOnce.Any(name => entry.Property(name) is { IsModified: true, OriginalValue: not null }))
                throw new InvalidOperationException($"{Name} data is immutable.");
            if (WriteOnce.Concat(Owned).Any(name => entry.Property(name).IsModified) && !authorized.Contains(id))
                throw new InvalidOperationException($"{Name} work changes require an ownership operation.");
        }
    }
}
