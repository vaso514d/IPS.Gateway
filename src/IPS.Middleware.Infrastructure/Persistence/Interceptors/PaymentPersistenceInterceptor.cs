using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IPS.Middleware.Infrastructure.Persistence.Interceptors;

internal sealed class PaymentPersistenceInterceptor : SaveChangesInterceptor
{
    internal static readonly PaymentPersistenceInterceptor Instance = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Validate(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Validate(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Validate(DbContext? context)
    {
        if (context is not TransactionDbContext { Phase: SavePhase.Entities } db) return;
        foreach (var entry in db.ChangeTracker.Entries<OutgoingPayment>())
        {
            if (entry.State == EntityState.Deleted)
                throw new InvalidOperationException("Payments cannot be deleted.");
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            foreach (var name in new[] { "MessageId", "ProtocolTransactionId" })
            {
                var identifier = entry.Property<string?>(name);
                if (entry.State == EntityState.Added && entry.Entity.MessageType == "pacs.008" &&
                    (identifier.CurrentValue is not { Length: 32 } value || !value.All(char.IsAsciiHexDigit)))
                    throw new InvalidOperationException("New pacs.008 intake requires generated protocol identifiers.");
                if (entry.State == EntityState.Modified && identifier.IsModified)
                    throw new InvalidOperationException("Protocol identifiers are immutable after intake.");
            }
            foreach (var name in new[] { "UnsignedXml", "SignedXml" })
            {
                var artifact = entry.Property<string?>(name);
                if (entry.State == EntityState.Added && artifact.CurrentValue is not null ||
                    entry.State == EntityState.Modified && artifact.IsModified &&
                    (artifact.OriginalValue is not null ||
                     !db.AuthorizedPreparation.TryGetValue((entry.Entity.Id, name), out var authorized) ||
                     !string.Equals(artifact.CurrentValue, authorized, StringComparison.Ordinal)))
                    throw new InvalidOperationException("Preparation artifacts require an authorized first write.");
            }
            if ((entry.Property<Guid?>("ClaimToken").OriginalValue is not null ||
                 entry.Property<Guid?>("ClaimToken").CurrentValue is not null) && !db.AuthorizedOwnership.Contains(entry.Entity.Id))
                throw new PersistenceConcurrencyException("A claimed payment requires an authorized ownership operation.");
            if (entry.State == EntityState.Modified && entry.Property<string>("RequestJson").IsModified)
                throw new InvalidOperationException("The original request is immutable.");
        }
    }
}
