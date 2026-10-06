using IPS.Middleware.Application.Payments;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Transactions;

namespace IPS.Middleware.Infrastructure.Persistence.Outgoing;

// Turns reportable state changes of outgoing payments into callback deliveries committed with the change itself.
internal static class OutgoingStatusOutbox
{
    internal static IReadOnlyList<OutgoingStatusDeliveryRow> Create(TransactionDbContext db)
    {
        var deliveries = new List<OutgoingStatusDeliveryRow>();
        var payments = db.ChangeTracker.Entries<OutgoingPayment>()
            .Select(entry => entry.Entity)
            .Where(payment => PaymentMessageTypes.IsOutgoing(payment.MessageType));

        foreach (var payment in payments)
        {
            var current = OutgoingStatusProjection.Read(db.Metadata(payment));
            foreach (var change in payment.PendingEvents.OfType<PaymentStateChanged>())
            {
                var status = current with
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

                deliveries.Add(new OutgoingStatusDeliveryRow
                {
                    PaymentId = status.PaymentId,
                    Sequence = status.Sequence,
                    PayloadJson = PaymentJson.Write(status),
                    NextAtUtc = status.StatusAtUtc
                });
            }
        }

        return deliveries;
    }
}
