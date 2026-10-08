using IPS.Middleware.Application.Diagnostics;
using IPS.Middleware.Domain;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Infrastructure.Diagnostics;

// Counts what a committed transaction decided, once per committed event, so a rolled-back save never counts and every
// outgoing and incoming flow is covered at the one place events become durable.
internal static class CommittedEventMetrics
{
    private const string Pacs008 = "pacs.008";

    internal static void Record(AggregateRoot aggregate, IEnumerable<DomainEvent> events)
    {
        foreach (var occurrence in events)
        {
            Record(aggregate, occurrence);
        }
    }

    private static void Record(AggregateRoot aggregate, DomainEvent occurrence)
    {
        switch (occurrence)
        {
            case PaymentStateChanged changed when aggregate is OutgoingPayment payment:
                PaymentMetrics.OutgoingStatusChanged(payment.MessageType, Name(changed.Status));
                break;
            case PaymentOutcomeObserved observed when aggregate is OutgoingPayment payment:
                PaymentMetrics.OutgoingOutcomeObserved(payment.MessageType, observed.Conflicting);
                break;
            case IncomingPaymentRegistered:
                PaymentMetrics.IncomingKindRegistered(Pacs008);
                break;
            case IncomingTransferRegistered registered:
                PaymentMetrics.IncomingKindRegistered(registered.Kind);
                break;
            case IncomingProcessingRecorded processing:
                PaymentMetrics.IncomingCoreEvent(Pacs008, Name(processing.Operation), Name(processing.CoreStatus));
                break;
            case IncomingTransferRecorded recorded when aggregate is IncomingTransfer transfer:
                PaymentMetrics.IncomingCoreEvent(transfer.Kind, Name(recorded.Operation), Name(recorded.CoreStatus));
                break;
        }
    }

    private static string Name<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();
}
