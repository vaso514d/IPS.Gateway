using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Transactions;

namespace IPS.Middleware.Infrastructure.Persistence.Outgoing;

internal static class OutgoingJournal
{
    internal static OutgoingMessageRow? Find(TransactionDbContext db, Guid paymentId, OutgoingMessageDirection direction) =>
        db.OutgoingMessages.Local.SingleOrDefault(row => IsPaymentMessage(row, paymentId, direction))
        ?? db.OutgoingMessages.SingleOrDefault(row => row.PaymentId == paymentId && row.InvestigationId == null && row.Direction == direction);

    private static bool IsPaymentMessage(OutgoingMessageRow row, Guid paymentId, OutgoingMessageDirection direction) =>
        row.PaymentId == paymentId && row.InvestigationId == null && row.Direction == direction;
}
