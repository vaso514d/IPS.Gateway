using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Persistence;

/// <summary>Write-once artifact slots shared by preparation and submission storage.</summary>
internal static class PaymentArtifacts
{
    /// <summary>The tracked entry of a persisted pacs.008 in Sending that the claim currently owns.</summary>
    internal static EntityEntry<OutgoingPayment> OwnedPacs008(
        this TransactionDbContext db, OutgoingPayment payment, TransactionClaim claim, DateTimeOffset now)
    {
        db.RequireUsable();
        ArgumentNullException.ThrowIfNull(claim);
        var entry = db.Entry(payment);
        if (entry.State is EntityState.Added or EntityState.Detached || payment.MessageType != Pacs008 ||
            payment.CurrentStatus != TransactionStatus.Sending || entry.TextOf(MessageId).CurrentValue is null)
            throw new InvalidOperationException("Payment artifacts require a persisted pacs.008 in Sending with stored identifiers.");
        if (!entry.HasLiveClaim(claim, now) || entry.ClaimTokenOf().OriginalValue != claim.Token)
            throw new PersistenceConcurrencyException("Payment artifacts require the current unexpired claim.");
        return entry;
    }

    /// <summary>Stage the first value of a slot; repeating identical content is a no-op, different content is refused.</summary>
    internal static void WriteOnce(this TransactionDbContext db, EntityEntry<OutgoingPayment> entry, string column, string value)
    {
        var slot = entry.TextOf(column);
        if (slot.CurrentValue is { } existing)
        {
            if (existing != value) throw new InvalidOperationException("Stored payment artifacts cannot be replaced.");
            return;
        }
        slot.CurrentValue = value;
        db.AuthorizedOwnership.Add(entry.Entity.Id);
        db.AuthorizedArtifacts.Add((entry.Entity.Id, column), value);
    }
}
