using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Persistence;

// Checkpoint writes are allowed only for the owner whose claim is already committed and still live.
internal static class PaymentOwnership
{
    internal static EntityEntry<OutgoingPaymentMetadata> OwnedPacs008(
        this TransactionDbContext db,
        OutgoingPayment payment,
        TransactionClaim claim,
        DateTimeOffset now,
        TransactionStatus requiredStatus)
    {
        var entry = db.Entry(db.Metadata(payment));
        var writable = entry.State != EntityState.Added
            && payment.MessageType == Pacs008
            && payment.CurrentStatus == requiredStatus
            && entry.Entity.MessageId is not null;
        if (!writable)
        {
            throw new InvalidOperationException($"Checkpoint writes require a persisted pacs.008 in {requiredStatus} with stored identifiers.");
        }

        var committedOwner = entry.Property(x => x.ClaimToken).OriginalValue == claim.Token;
        if (!entry.Entity.HasLiveClaim(claim, now) || !committedOwner)
        {
            throw new PersistenceConcurrencyException("Checkpoint writes require the current unexpired claim.");
        }

        return entry;
    }

    // Every checkpoint advances the version, so a stale owner's concurrent write fails on the row version.
    internal static async Task<IncomingPayment> OwnedIncomingPaymentAsync(
        this TransactionDbContext db,
        IncomingPaymentClaim claim,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var metadata = await db.IncomingMetadata
            .Include(x => x.Payment)
            .SingleOrDefaultAsync(x => x.Id == claim.PaymentId, cancellationToken)
            ?? throw new PersistenceConcurrencyException("The incoming payment no longer exists.");

        var entry = db.Entry(metadata);
        var claimChangedInScope = entry.Property(x => x.ClaimToken).IsModified;
        if (entry.State == EntityState.Added || claimChangedInScope || !metadata.HasLiveClaim(claim, now))
        {
            throw new PersistenceConcurrencyException("The incoming payment owner is stale or expired.");
        }

        metadata.CheckpointVersion = checked(metadata.CheckpointVersion + 1);
        return metadata.Payment;
    }
}
