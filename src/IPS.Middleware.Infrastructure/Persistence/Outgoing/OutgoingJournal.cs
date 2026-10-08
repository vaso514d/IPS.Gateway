using System.Linq.Expressions;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Transactions;

namespace IPS.Middleware.Infrastructure.Persistence.Outgoing;

internal static class OutgoingJournal
{
    private const string InconclusiveResponse = "The response did not establish a valid correlated final outcome.";

    // The initial pacs.008 exchange, as opposed to an investigation or resend exchange.
    internal static readonly Expression<Func<OutgoingMessageRow, bool>> IsInitial = row => row.InvestigationId == null && row.ResendId == null;

    private static readonly Func<OutgoingMessageRow, bool> IsInitialRow = IsInitial.Compile();

    internal static OutgoingMessageRow? Find(TransactionDbContext db, Guid paymentId, OutgoingMessageDirection direction) =>
        db.OutgoingMessages.Local.SingleOrDefault(row => row.PaymentId == paymentId && row.Direction == direction && IsInitialRow(row))
        ?? db.OutgoingMessages
            .Where(IsInitial)
            .SingleOrDefault(row => row.PaymentId == paymentId && row.Direction == direction);

    // Only a validated, correlated response establishes its protocol definition.
    internal static void Consume(OutgoingMessageRow response, bool conclusive, string? failure, DateTimeOffset now)
    {
        if (response.Status != MessageJournalStatus.Received)
        {
            throw new InvalidOperationException("Only unconsumed response evidence can be interpreted.");
        }

        response.Status = conclusive ? MessageJournalStatus.Processed : MessageJournalStatus.Failed;
        response.MessageDefinition = conclusive ? PaymentMessageTypes.Pacs002Definition : null;
        response.ProcessedAtUtc = now.ToUniversalTime();
        response.Failure = conclusive ? null : failure ?? InconclusiveResponse;
    }
}
