using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Persistence;

/// <summary>Write-once artifact slots shared by preparation and submission storage.</summary>
internal static class PaymentArtifacts
{
    /// <summary>The tracked entry of a persisted pacs.008 in Sending that the claim currently owns.</summary>
    internal static EntityEntry<OutgoingPaymentMetadata> OwnedPacs008(
        this TransactionDbContext db, OutgoingPayment payment, TransactionClaim claim, DateTimeOffset now)
    {
        db.RequireUsable();
        ArgumentNullException.ThrowIfNull(claim);
        var entry = db.Entry(db.Metadata(payment));
        if (entry.State is EntityState.Added or EntityState.Detached || payment.MessageType != Pacs008 ||
            payment.CurrentStatus != TransactionStatus.Sending || entry.Entity.MessageId is null)
        {
            throw new InvalidOperationException("Payment artifacts require a persisted pacs.008 in Sending with stored identifiers.");
        }

        if (!entry.Entity.HasLiveClaim(claim, now) || entry.Property(p => p.ClaimToken).OriginalValue != claim.Token)
        {
            throw new PersistenceConcurrencyException("Payment artifacts require the current unexpired claim.");
        }

        return entry;
    }

    /// <summary>Stage the first value of a slot; repeating identical content is a no-op, different content is refused.</summary>
    internal static void WriteUnsignedXml(this TransactionDbContext db, EntityEntry<OutgoingPaymentMetadata> entry, string value)
    {
        var slot = entry.Property(p => p.UnsignedXml);
        if (slot.CurrentValue is { } existing)
        {
            if (existing != value)
            {
                throw new InvalidOperationException("Stored payment artifacts cannot be replaced.");
            }

            return;
        }
        slot.CurrentValue = value;
        db.Changes.AuthorizedOwnership.Add(entry.Entity.Id);
        db.Changes.AuthorizedArtifacts.Add(entry.Entity.Id, value);
    }
}
