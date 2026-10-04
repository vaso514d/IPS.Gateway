using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Persistence.Interceptors;

/// <summary>Receipt, incoming payment and reply data never change; only ownership operations change their work fields.</summary>
internal sealed class InboundPersistenceInterceptor : SaveRuleInterceptor
{
    internal static readonly InboundPersistenceInterceptor Instance = new();

    private static readonly Rules Receipt = new("Inbound receipt",
        Immutable: [nameof(InboundJournalEntry.ParticipantBic), nameof(InboundJournalEntry.Sequence), nameof(InboundJournalEntry.MessageType),
            nameof(InboundJournalEntry.RawXml), nameof(InboundJournalEntry.PossibleDuplicate), nameof(InboundJournalEntry.ReceivedAtUtc)],
        WriteOnce: [nameof(InboundJournalEntry.HoldReason), nameof(InboundJournalEntry.IncomingPaymentId), nameof(InboundJournalEntry.OriginalJson)],
        Owned: [nameof(InboundJournalEntry.Status), nameof(InboundJournalEntry.NextActionAtUtc),
            nameof(InboundJournalEntry.ClaimToken), nameof(InboundJournalEntry.ClaimExpiresAtUtc), nameof(InboundJournalEntry.ReplyCheckpoint)]);

    private static readonly Rules Payment = new("Incoming payment",
        Immutable: [nameof(IncomingPayment.ParticipantBic), nameof(IncomingPayment.EndToEndId), nameof(IncomingPayment.RegisteredAtUtc), RequestJson, IncomingPaymentColumns.ContextJson],
        WriteOnce: [IncomingPaymentColumns.ReconciliationDeadlineUtc],
        Owned: [ClaimToken, ClaimExpiresAtUtc, NextActionAtUtc]);

    private static readonly string[] ReplyArtifacts =
        [nameof(IncomingReplyRow.UnsignedXml), nameof(IncomingReplyRow.MessageXml), nameof(IncomingReplyRow.MessageKind)];

    protected override void Apply(TransactionDbContext db)
    {
        if (db.Phase != SavePhase.Entities) return;
        foreach (var entry in db.ChangeTracker.Entries<InboundJournalEntry>()) Receipt.Check(entry, entry.Entity.Id, db.AuthorizedInboundWork);
        foreach (var entry in db.ChangeTracker.Entries<IncomingPayment>())
        {
            Payment.Check(entry, entry.Entity.Id, db.AuthorizedIncomingPaymentWork);
            if (entry.State == EntityState.Modified && !db.AuthorizedIncomingProcessing.Contains(entry.Entity.Id) &&
                entry.Properties.Any(p => p.IsModified && (!p.Metadata.IsShadowProperty() ||
                    p.Metadata.Name is IncomingPaymentColumns.CheckpointVersion or IncomingPaymentColumns.FollowUpAtUtc or IncomingPaymentColumns.ReconciliationDeadlineUtc)))
                throw new InvalidOperationException("Incoming processing changes require payment ownership.");
        }
        foreach (var call in db.ChangeTracker.Entries<IncomingCoreCallRow>())
            if (IsFencedUpdate(call, "CBS call evidence", db.AuthorizedIncomingCalls.Contains(call.Entity.Id) && db.AuthorizedIncomingProcessing.Contains(call.Entity.PaymentId)) &&
                RewritesEvidence(call, nameof(IncomingCoreCallRow.CompletionJson), nameof(IncomingCoreCallRow.Consumed)))
                throw new InvalidOperationException("CBS call evidence is write-once.");
        foreach (var reply in db.ChangeTracker.Entries<IncomingReplyRow>())
            if (IsFencedUpdate(reply, "Reply artifacts", db.AuthorizedReplies.Contains(reply.Entity.JournalId)) &&
                (reply.Property(r => r.EnvelopeJson).IsModified || ReplyArtifacts.Any(name => reply.Property(name) is { IsModified: true, OriginalValue: not null })))
                throw new InvalidOperationException("Reply artifacts are immutable once saved.");
        foreach (var attempt in db.ChangeTracker.Entries<IncomingReplyAttemptRow>())
            if (IsFencedUpdate(attempt, "Reply attempt evidence", db.AuthorizedReplies.Contains(attempt.Entity.JournalId)) &&
                RewritesEvidence(attempt, nameof(IncomingReplyAttemptRow.CompletionJson), nameof(IncomingReplyAttemptRow.Consumed)))
                throw new InvalidOperationException("Reply attempt evidence is write-once.");
    }

    /// <summary>Evidence rows are never deleted and change only under a fenced checkpoint; true for a modification to check.</summary>
    private static bool IsFencedUpdate(EntityEntry entry, string name, bool authorized)
    {
        if (entry.State == EntityState.Deleted) throw new InvalidOperationException($"{name} cannot be deleted.");
        if (entry.State is not (EntityState.Added or EntityState.Modified)) return false;
        if (!authorized) throw new InvalidOperationException($"{name} requires a fenced checkpoint.");
        return entry.State == EntityState.Modified;
    }

    // Remote-call evidence: the completion is written once, then consumed once; nothing else changes.
    private static bool RewritesEvidence(EntityEntry entry, string completion, string consumed) => entry.Properties.Any(p => p.IsModified &&
        (p.Metadata.Name != completion && p.Metadata.Name != consumed ||
         p.Metadata.Name == completion && p.OriginalValue is not null ||
         p.Metadata.Name == consumed && Equals(p.OriginalValue, true)));

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
