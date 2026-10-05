using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Persistence.Interceptors;
/// <summary>Prepares delivery records from outcome changes; never transitions a payment or dispatches a callback.</summary>
internal sealed class OutgoingStatusDeliveryInterceptor : SaveRuleInterceptor
{
    internal static readonly OutgoingStatusDeliveryInterceptor Instance = new();
    protected override void Apply(TransactionDbContext db)
    {
        if (db.Changes.Phase == SavePhase.Evidence)
        {
            db.OutgoingStatusDeliveries.AddRange(db.Changes.PendingOutgoingStatuses);
            return;
        }

        if (db.Changes.Phase != SavePhase.Entities)
        {
            return;
        }

        foreach (var entry in db.ChangeTracker.Entries<OutgoingStatusDeliveryRow>())
        {
            if (entry.State == EntityState.Unchanged)
            {
                continue;
            }

            if (entry.State != EntityState.Modified ||
                entry.Property(p => p.PayloadJson).IsModified || entry.Property(p => p.PayloadVersion).IsModified ||
                !db.Changes.StatusChanges.Matches(entry) ||
                !db.ChangeTracker.Entries<OutgoingPaymentMetadata>().Any(p => p.Entity.Id == entry.Entity.PaymentId && p.State == EntityState.Modified))
            {
                throw new InvalidOperationException("Delivery evidence requires an authorized version-checked operation; payloads are immutable.");
            }
        }

        foreach (var entry in db.ChangeTracker.Entries<OutgoingPayment>())
        {
            if (entry.Entity.MessageType != PaymentColumns.Pacs008)
            {
                continue;
            }

            var current = OutgoingStatusProjection.Read(db.Metadata(entry.Entity));
            foreach (var change in entry.Entity.PendingEvents.OfType<PaymentStateChanged>())
            {
                var status = new Application.Payments.StatusDelivery.OutgoingStatus(current)
                {
                    Sequence = change.Sequence,
                    Status = change.Status,
                    StatusAtUtc = change.OccurredAtUtc,
                    Details = change.Details
                };
                if (!status.IsReportable)
                {
                    continue;
                }

                db.Changes.PendingOutgoingStatuses.Add(new()
                {
                    PaymentId = status.PaymentId,
                    Sequence = status.Sequence,
                    PayloadJson = PaymentJson.Write(status),
                    NextAtUtc = status.StatusAtUtc
                });
            }
        }
    }
}
