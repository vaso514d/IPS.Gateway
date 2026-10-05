using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Domain.Transactions;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Persistence.Outgoing;

internal static class OutgoingStatusProjection
{
    internal static OutgoingStatus Read(EntityEntry<OutgoingPayment> entry)
    {
        var payment = entry.Entity;
        return new(payment.Id, payment.CurrentSequence, payment.MessageType, payment.ClientReference,
            payment.CurrentStatus, payment.CurrentStatusAtUtc, payment.Current.Details, entry.TextOf(MessageId).CurrentValue,
            PaymentJson.ReadAccepted(entry.TextOf(AcceptedJson).CurrentValue)?.Payment.EndToEndId);
    }
}
