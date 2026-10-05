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
    protected override void Apply(TransactionDbContext db)
    {
        if (db.Changes.Phase != SavePhase.Entities)
        {
            return;
        }

        foreach (var entry in db.ChangeTracker.Entries<InboundJournalEntry>())
        {
            CheckReceipt(entry, db.Changes.AuthorizedInboundWork.Contains(entry.Entity.Id));
        }

        foreach (var entry in db.ChangeTracker.Entries<IncomingPayment>())
        {
            if (entry.State == EntityState.Modified && !db.IncomingMetadata.Local.Any(p => p.Id == entry.Entity.Id))
            {
                throw new InvalidOperationException("Load the payment with its metadata before saving changes.");
            }

            CheckPayment(entry, db.Changes.AuthorizedIncomingProcessing.Contains(entry.Entity.Id));
        }

        foreach (var entry in db.ChangeTracker.Entries<IncomingPaymentMetadata>())
        {
            CheckPaymentMetadata(entry, db.Changes.AuthorizedIncomingPaymentWork.Contains(entry.Entity.Id),
                db.Changes.AuthorizedIncomingProcessing.Contains(entry.Entity.Id));
        }

        foreach (var call in db.ChangeTracker.Entries<IncomingCoreCallRow>())
        {
            if (IsFencedUpdate(call, "CBS call evidence", db.Changes.AuthorizedIncomingCalls.Contains(call.Entity.Id) && db.Changes.AuthorizedIncomingProcessing.Contains(call.Entity.PaymentId)) &&
                RewritesEvidence(call, nameof(IncomingCoreCallRow.CompletionJson), nameof(IncomingCoreCallRow.Consumed)))
            {
                throw new InvalidOperationException("CBS call evidence is write-once.");
            }
        }

        foreach (var reply in db.ChangeTracker.Entries<IncomingReplyRow>())
        {
            if (IsFencedUpdate(reply, "Reply artifacts", db.Changes.AuthorizedReplies.Contains(reply.Entity.JournalId)) &&
                (reply.Property(r => r.EnvelopeJson).IsModified || Replaced(reply.Property(r => r.UnsignedXml), reply.Property(r => r.MessageXml), reply.Property(r => r.MessageKind))))
            {
                throw new InvalidOperationException("Reply artifacts are immutable once saved.");
            }
        }

        foreach (var attempt in db.ChangeTracker.Entries<IncomingReplyAttemptRow>())
        {
            if (IsFencedUpdate(attempt, "Reply attempt evidence", db.Changes.AuthorizedReplies.Contains(attempt.Entity.JournalId)) &&
                RewritesEvidence(attempt, nameof(IncomingReplyAttemptRow.CompletionJson), nameof(IncomingReplyAttemptRow.Consumed)))
            {
                throw new InvalidOperationException("Reply attempt evidence is write-once.");
            }
        }
    }

    /// <summary>Evidence rows are never deleted and change only under a fenced checkpoint; true for a modification to check.</summary>
    private static bool IsFencedUpdate(EntityEntry entry, string name, bool authorized)
    {
        if (entry.State == EntityState.Deleted)
        {
            throw new InvalidOperationException($"{name} cannot be deleted.");
        }

        if (entry.State is not (EntityState.Added or EntityState.Modified))
        {
            return false;
        }

        if (!authorized)
        {
            throw new InvalidOperationException($"{name} requires a fenced checkpoint.");
        }

        return entry.State == EntityState.Modified;
    }

    // Remote-call evidence: the completion is written once, then consumed once; nothing else changes.
    private static bool RewritesEvidence(EntityEntry entry, string completion, string consumed) => entry.Properties.Any(p => p.IsModified &&
        (p.Metadata.Name != completion && p.Metadata.Name != consumed ||
         p.Metadata.Name == completion && p.OriginalValue is not null ||
         p.Metadata.Name == consumed && Equals(p.OriginalValue, true)));
    private static void CheckReceipt(EntityEntry<InboundJournalEntry> entry, bool owned)
    {
        if (!IsUpdate(entry, "Inbound receipt"))
        {
            return;
        }

        var writeOnce = new PropertyEntry[]
        {
            entry.Property(p => p.HoldReason), entry.Property(p => p.IncomingPaymentId), entry.Property(p => p.OriginalJson)
        };
        if (Changed(entry.Property(p => p.ParticipantBic), entry.Property(p => p.Sequence), entry.Property(p => p.MessageType),
                entry.Property(p => p.RawXml), entry.Property(p => p.PossibleDuplicate), entry.Property(p => p.ReceivedAtUtc)) ||
            Replaced(writeOnce))
        {
            throw new InvalidOperationException("Inbound receipt data is immutable.");
        }

        if (!owned && (Changed(writeOnce) || Changed(entry.Property(p => p.Status), entry.Property(p => p.NextActionAtUtc),
            entry.Property(p => p.ClaimToken), entry.Property(p => p.ClaimExpiresAtUtc), entry.Property(p => p.ReplyCheckpoint))))
        {
            throw new InvalidOperationException("Inbound receipt work changes require an ownership operation.");
        }
    }

    private static void CheckPayment(EntityEntry<IncomingPayment> entry, bool processing)
    {
        if (!IsUpdate(entry, "Incoming payment"))
        {
            return;
        }

        if (Changed(entry.Property(p => p.ParticipantBic), entry.Property(p => p.EndToEndId), entry.Property(p => p.RegisteredAtUtc)))
        {
            throw new InvalidOperationException("Incoming payment data is immutable.");
        }

        if (!processing && entry.Properties.Any(p => p.IsModified && !p.Metadata.IsShadowProperty()))
        {
            throw new InvalidOperationException("Incoming processing changes require payment ownership.");
        }
    }

    private static void CheckPaymentMetadata(EntityEntry<IncomingPaymentMetadata> entry, bool owned, bool processing)
    {
        if (!IsUpdate(entry, "Incoming payment metadata"))
        {
            return;
        }

        var deadline = entry.Property(p => p.ReconciliationDeadlineUtc);
        if (Changed(entry.Property(p => p.RequestJson), entry.Property(p => p.ContextJson)) || Replaced(deadline))
        {
            throw new InvalidOperationException("Incoming payment data is immutable.");
        }

        if (!owned && Changed(deadline, entry.Property(p => p.ClaimToken), entry.Property(p => p.ClaimExpiresAtUtc), entry.Property(p => p.NextActionAtUtc)))
        {
            throw new InvalidOperationException("Incoming payment work changes require an ownership operation.");
        }

        if (!processing && Changed(deadline, entry.Property(p => p.CheckpointVersion), entry.Property(p => p.FollowUpAtUtc)))
        {
            throw new InvalidOperationException("Incoming processing changes require payment ownership.");
        }
    }

    private static bool IsUpdate(EntityEntry entry, string name)
    {
        if (entry.State == EntityState.Deleted)
        {
            throw new InvalidOperationException($"{name} rows cannot be deleted.");
        }

        return entry.State == EntityState.Modified;
    }

    private static bool Changed(params PropertyEntry[] properties) => properties.Any(p => p.IsModified);
    private static bool Replaced(params PropertyEntry[] properties) => properties.Any(p => p.IsModified && p.OriginalValue is not null);
}
