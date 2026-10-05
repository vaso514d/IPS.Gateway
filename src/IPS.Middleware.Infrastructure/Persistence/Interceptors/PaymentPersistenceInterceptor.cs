using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace IPS.Middleware.Infrastructure.Persistence.Interceptors;

internal sealed class PaymentPersistenceInterceptor : SaveRuleInterceptor
{
    internal static readonly PaymentPersistenceInterceptor Instance = new();

    protected override void Apply(TransactionDbContext db)
    {
        if (db.Changes.Phase != SavePhase.Entities)
        {
            return;
        }

        foreach (var payment in db.ChangeTracker.Entries<OutgoingPayment>())
        {
            if (payment.State == EntityState.Modified && !db.OutgoingMetadata.Local.Any(p => p.Id == payment.Entity.Id))
            {
                throw new InvalidOperationException("Load the payment with its metadata before saving changes.");
            }

            if (payment.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Payments cannot be deleted.");
            }
        }

        foreach (var entry in db.ChangeTracker.Entries<OutgoingPaymentMetadata>())
        {
            if (entry.State == EntityState.Deleted)
            {
                throw new InvalidOperationException("Payment metadata cannot be deleted.");
            }

            var stateChanged = db.Entry(entry.Entity.Payment).State == EntityState.Modified;
            if (entry.State is not (EntityState.Added or EntityState.Modified) && !stateChanged)
            {
                continue;
            }

            CheckIdentifiers(entry);
            CheckUnsignedXml(db, entry);
            CheckOwnership(db, entry);
            if (entry.State == EntityState.Modified &&
                (entry.Property(p => p.RequestJson).IsModified || entry.Property(p => p.AcceptedJson).IsModified))
            {
                throw new InvalidOperationException("The original request and accepted snapshot are immutable.");
            }
        }
    }

    private static void CheckIdentifiers(EntityEntry<OutgoingPaymentMetadata> entry)
    {
        foreach (var identifier in new[] { entry.Property(p => p.MessageId), entry.Property(p => p.ProtocolTransactionId) })
        {
            if (entry.State == EntityState.Added && entry.Entity.Payment.MessageType == PaymentColumns.Pacs008 &&
                (identifier.CurrentValue is not { Length: 32 } value || !value.All(char.IsAsciiHexDigit)))
            {
                throw new InvalidOperationException("New pacs.008 intake requires generated protocol identifiers.");
            }

            if (entry.State == EntityState.Modified && identifier.IsModified)
            {
                throw new InvalidOperationException("Protocol identifiers are immutable after intake.");
            }
        }
    }

    private static void CheckUnsignedXml(TransactionDbContext db, EntityEntry<OutgoingPaymentMetadata> entry)
    {
        var artifact = entry.Property(p => p.UnsignedXml);
        var invalid = entry.State == EntityState.Added
            ? artifact.CurrentValue is not null
            : artifact.IsModified && (artifact.OriginalValue is not null ||
                !db.Changes.AuthorizedArtifacts.TryGetValue(entry.Entity.Id, out var authorized) || artifact.CurrentValue != authorized);
        if (invalid)
        {
            throw new InvalidOperationException("Payment artifacts require an authorized first write.");
        }
    }

    private static void CheckOwnership(TransactionDbContext db, EntityEntry<OutgoingPaymentMetadata> entry)
    {
        var token = entry.Property(p => p.ClaimToken);
        if ((token.OriginalValue is not null || token.CurrentValue is not null) && !db.Changes.AuthorizedOwnership.Contains(entry.Entity.Id))
        {
            throw new PersistenceConcurrencyException("A claimed payment requires an authorized ownership operation.");
        }
    }
}
