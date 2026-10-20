using IPS.Middleware.Domain;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Domain.Transactions;
using Microsoft.Extensions.Logging;

namespace IPS.Middleware.Infrastructure.Diagnostics;

// One console line per committed step of every payment, so an operator can follow a send or a receipt without the
// database: received, processing steps, each status change with its source and reason, and the incoming decisions.
// Written after the commit, like the metrics, so a rolled-back save never logs a step that did not happen.
internal static class CommittedEventLog
{
    internal static void Write(ILogger logger, AggregateRoot aggregate, IEnumerable<DomainEvent> events)
    {
        foreach (var occurrence in events)
        {
            Write(logger, aggregate, occurrence);
        }
    }

    private static void Write(ILogger logger, AggregateRoot aggregate, DomainEvent occurrence)
    {
        switch (occurrence)
        {
            case PaymentReceived received:
                logger.LogInformation("Outgoing {MessageType} {ClientReference} received from the core system (payment {PaymentId})",
                    received.MessageType, received.ClientReference, aggregate.Id);
                break;
            case PaymentProcessingObserved observed when aggregate is OutgoingPayment payment:
                logger.LogInformation("Outgoing {MessageType} {ClientReference}: {Step}",
                    payment.MessageType, payment.ClientReference, observed.Step);
                break;
            case PaymentProcessingFailed failed when aggregate is OutgoingPayment payment:
                logger.LogWarning("Outgoing {MessageType} {ClientReference}: {Step} failed: {Description}",
                    payment.MessageType, payment.ClientReference, failed.Step, failed.Description);
                break;
            case PaymentStateChanged changed when aggregate is OutgoingPayment payment:
                logger.Log(Level(changed.Status), "Outgoing {MessageType} {ClientReference}: {Previous} -> {Status} by {Source}{Reason}",
                    payment.MessageType, payment.ClientReference, changed.PreviousStatus, changed.Status, changed.Source, Reason(changed.Details));
                break;
            case PaymentOutcomeObserved { Conflicting: true } observed when aggregate is OutgoingPayment payment:
                logger.LogWarning("Outgoing {MessageType} {ClientReference}: {Source} reported {Reported}, which conflicts with {Current}{Reason}",
                    payment.MessageType, payment.ClientReference, observed.Source, observed.ReportedStatus, observed.CurrentStatus, Reason(observed.Details));
                break;
            case IncomingPaymentRegistered registered:
                logger.LogInformation("Incoming pacs.008 {EndToEndId} received from IPS (payment {PaymentId})", registered.EndToEndId, aggregate.Id);
                break;
            case IncomingProcessingRecorded recorded when aggregate is IncomingPayment payment:
                logger.Log(Level(recorded), "Incoming pacs.008 {EndToEndId}: {Operation}, core {CoreStatus}{Decision}",
                    payment.EndToEndId, recorded.Operation, recorded.CoreStatus, Decision(recorded.IpsDecision));
                break;
            case IncomingTransferRegistered registered:
                logger.LogInformation("Incoming {Kind} {Key} received from IPS (transfer {TransferId})", registered.Kind, registered.Key, aggregate.Id);
                break;
            case IncomingTransferRecorded recorded when aggregate is IncomingTransfer transfer:
                logger.Log(Level(recorded.CoreStatus), "Incoming {Kind} {Key}: {Operation}, core {CoreStatus}{Reason}",
                    transfer.Kind, transfer.Key, recorded.Operation, recorded.CoreStatus, Reason(recorded.ReasonCode, null, recorded.Description));
                break;
        }
    }

    private static LogLevel Level(TransactionStatus status) => status switch
    {
        TransactionStatus.Rejected or TransactionStatus.NotSent or TransactionStatus.Uncertain or TransactionStatus.ManualReview
            or TransactionStatus.CoreUnknown => LogLevel.Warning,
        _ => LogLevel.Information
    };

    private static LogLevel Level(IncomingProcessingRecorded recorded) =>
        recorded.IpsDecision is { Accepted: false } || recorded.Operation == IncomingProcessingOperation.OutcomeConflictObserved
            ? LogLevel.Warning
            : Level(recorded.CoreStatus);

    private static LogLevel Level(CoreOutcome core) => core is CoreOutcome.Rejected or CoreOutcome.Unknown ? LogLevel.Warning : LogLevel.Information;

    private static string Reason(PaymentDetails details) => Reason(details.ReasonCode, details.IpsInternalCode, details.Description);

    private static string Reason(string? code, int? ipsCode, string? description)
    {
        var parts = new[] { code, ipsCode?.ToString(System.Globalization.CultureInfo.InvariantCulture), description }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        return parts.Length == 0 ? string.Empty : " (" + string.Join(", ", parts) + ")";
    }

    private static string Decision(IncomingIpsDecision? decision) => decision switch
    {
        null => string.Empty,
        { Accepted: true } => ", IPS answered ACCP",
        _ => $", IPS answered RJCT{Reason(decision.ReasonCode, null, decision.Description)}"
    };
}
