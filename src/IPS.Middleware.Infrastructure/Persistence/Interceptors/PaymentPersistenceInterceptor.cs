using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Persistence.Interceptors;

internal sealed class PaymentPersistenceInterceptor : SaveRuleInterceptor
{
    internal static readonly PaymentPersistenceInterceptor Instance = new();

    protected override void Apply(TransactionDbContext db)
    {
        if (db.Phase != SavePhase.Entities) return;
        foreach (var entry in db.ChangeTracker.Entries<OutgoingPayment>())
        {
            if (entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Payments cannot be deleted.");
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            CheckIdentifiers(entry);
            CheckArtifacts(db, entry);
            CheckOwnership(db, entry);
            if (entry.State == EntityState.Modified && entry.Property<string>(RequestJson).IsModified)
                throw new InvalidOperationException("The original request is immutable.");
        }
    }

    private static void CheckIdentifiers(EntityEntry<OutgoingPayment> entry)
    {
        foreach (var identifier in new[] { entry.TextOf(MessageId), entry.TextOf(ProtocolTransactionId) })
        {
            if (entry.State == EntityState.Added && entry.Entity.MessageType == Pacs008 &&
                (identifier.CurrentValue is not { Length: 32 } value || !value.All(char.IsAsciiHexDigit)))
                throw new InvalidOperationException("New pacs.008 intake requires generated protocol identifiers.");
            if (entry.State == EntityState.Modified && identifier.IsModified)
                throw new InvalidOperationException("Protocol identifiers are immutable after intake.");
        }
    }

    private static void CheckArtifacts(TransactionDbContext db, EntityEntry<OutgoingPayment> entry)
    {
        foreach (var column in new[] { UnsignedXml, SignedXml })
        {
            var artifact = entry.TextOf(column);
            var invalid = entry.State == EntityState.Added
                ? artifact.CurrentValue is not null
                : artifact.IsModified && (artifact.OriginalValue is not null ||
                    !db.AuthorizedPreparation.TryGetValue((entry.Entity.Id, column), out var authorized) ||
                    !string.Equals(artifact.CurrentValue, authorized, StringComparison.Ordinal));
            if (invalid) throw new InvalidOperationException("Preparation artifacts require an authorized first write.");
        }
    }

    private static void CheckOwnership(TransactionDbContext db, EntityEntry<OutgoingPayment> entry)
    {
        var token = entry.ClaimTokenOf();
        if ((token.OriginalValue is not null || token.CurrentValue is not null) && !db.AuthorizedOwnership.Contains(entry.Entity.Id))
            throw new PersistenceConcurrencyException("A claimed payment requires an authorized ownership operation.");
    }
}
