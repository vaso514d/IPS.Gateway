using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Persistence.Outgoing;

internal static class OutgoingJournal
{
    internal static OutgoingMessageRow? Find(TransactionDbContext db, Guid paymentId, OutgoingMessageDirection direction) =>
        db.OutgoingMessages.Local.SingleOrDefault(p => p.PaymentId == paymentId && p.InvestigationId == null && p.Direction == direction) ??
        db.OutgoingMessages.SingleOrDefault(p => p.PaymentId == paymentId && p.InvestigationId == null && p.Direction == direction);

    internal static void Authorize(TransactionDbContext db, OutgoingPayment payment, OutgoingMessageRow row)
    {
        // Force a version-checked parent UPDATE even when only a technical record changed.
        db.Entry(db.Metadata(payment)).Property(p => p.NextActionAtUtc).IsModified = true;
        db.Changes.AuthorizedOwnership.Add(payment.Id);
        db.Changes.MessageChanges.Authorize(db.Entry(row));
    }
}
