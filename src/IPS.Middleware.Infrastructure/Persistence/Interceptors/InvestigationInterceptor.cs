using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Persistence.Interceptors;

internal sealed class InvestigationInterceptor : SaveRuleInterceptor
{
    internal static readonly InvestigationInterceptor Instance = new();
    protected override void Apply(TransactionDbContext db)
    {
        if (db.Changes.Phase == SavePhase.Evidence)
        {
            db.Changes.InvestigationChanges.Restore(db);
            return;
        }
        if (db.Changes.Phase != SavePhase.Entities)
        {
            return;
        }

        foreach (var entry in db.ChangeTracker.Entries<InvestigationRow>().ToArray())
        {
            if (entry.State == EntityState.Unchanged)
            {
                continue;
            }

            if (entry.State == EntityState.Modified)
            {
                string[] mutable = [nameof(InvestigationRow.UnsignedXml), nameof(InvestigationRow.Outcome), nameof(InvestigationRow.DetailsJson), nameof(InvestigationRow.TransportFailure), nameof(InvestigationRow.CompletedAtUtc)];
                if (entry.Properties.Any(p => p.IsModified && (!mutable.Contains(p.Metadata.Name) || p.OriginalValue is not null)))
                {
                    throw new InvalidOperationException("Investigation evidence is write-once.");
                }
            }
            if (entry.State == EntityState.Deleted || !db.Changes.InvestigationChanges.Matches(entry) ||
                !db.ChangeTracker.Entries<OutgoingPaymentMetadata>().Any(p => p.Entity.Id == entry.Entity.PaymentId && p.State == EntityState.Modified))
            {
                throw new InvalidOperationException("Investigation writes require authorized evidence and a version-checked parent.");
            }

            db.Changes.InvestigationChanges.Defer(entry);
        }
    }
}
