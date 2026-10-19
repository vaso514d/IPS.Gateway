using IPS.Middleware.Application.Payments;
using IPS.Middleware.Domain;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Infrastructure.Persistence.History;

// Builds the status history rows of a transfer from the events its save commits. Outgoing events carry their status;
// an incoming payment's status is derived from its core outcome, IPS decision and follow-up (specification 020).
internal static class TransferStatusHistory
{
    internal static IEnumerable<TransferStatusRow> Rows(AggregateRoot aggregate, IEnumerable<DomainEvent> events) => aggregate switch
    {
        OutgoingPayment payment when PaymentMessageTypes.IsPacs008(payment.MessageType) => events
            .Select(Outgoing)
            .OfType<Entry>()
            .Select(entry => Row(payment.Id, TransactionDirection.Outgoing, entry)),
        IncomingPayment payment => events
            .Select(Incoming)
            .OfType<Entry>()
            .Select(entry => Row(payment.Id, TransactionDirection.Incoming, entry)),
        _ => []
    };

    // A repeated or conflicting IPS report and a recall refusal change no status, so they are not history rows.
    private static Entry? Outgoing(DomainEvent occurrence) => occurrence switch
    {
        PaymentReceived received => new(TransactionStatus.Received, StatusSource.Gateway, null, received.OccurredAtUtc),
        PaymentStateChanged changed => new(changed.Status, changed.Source, null, changed.OccurredAtUtc,
            changed.Details.ReasonCode, changed.Details.IpsInternalCode, changed.Details.Description),
        PaymentProcessingObserved observed => new(observed.Status, StatusSource.Gateway, observed.Step, observed.OccurredAtUtc),
        PaymentProcessingFailed failed => new(failed.Status, StatusSource.Gateway, failed.Step, failed.OccurredAtUtc,
            Description: failed.Description),
        _ => null
    };

    // A final core outcome observed again changes nothing; a conflicting one moves the payment to manual review.
    private static Entry? Incoming(DomainEvent occurrence) => occurrence switch
    {
        IncomingPaymentRegistered registered => new(TransactionStatus.Received, StatusSource.Ips, null, registered.OccurredAtUtc),
        IncomingProcessingRecorded { Operation: IncomingProcessingOperation.SubmissionStarted } recorded =>
            new(IncomingStatus(recorded.CoreStatus, recorded.IpsDecision, recorded.FollowUp), StatusSource.Gateway,
                ProcessingStep.SentToCore, recorded.OccurredAtUtc),
        IncomingProcessingRecorded { Operation: IncomingProcessingOperation.CoreOutcomeRecorded } recorded =>
            new(IncomingStatus(recorded.CoreStatus, recorded.IpsDecision, recorded.FollowUp), StatusSource.Core,
                ProcessingStep.CoreResponded, recorded.OccurredAtUtc, recorded.CurrentResult?.ReasonCode, null,
                recorded.CurrentResult?.Description),
        IncomingProcessingRecorded { Operation: IncomingProcessingOperation.IpsDecisionRecorded } recorded =>
            new(IncomingStatus(recorded.CoreStatus, recorded.IpsDecision, recorded.FollowUp), StatusSource.Gateway,
                ProcessingStep.RepliedToIps, recorded.OccurredAtUtc, recorded.IpsDecision?.ReasonCode, null,
                recorded.IpsDecision?.Description),
        IncomingProcessingRecorded { Operation: IncomingProcessingOperation.OutcomeConflictObserved } recorded =>
            new(IncomingStatus(recorded.CoreStatus, recorded.IpsDecision, recorded.FollowUp), StatusSource.Core,
                ProcessingStep.CoreResponded, recorded.OccurredAtUtc, recorded.ObservedResult?.ReasonCode, null,
                recorded.ObservedResult?.Description),
        IncomingReconciliationRecorded reconciled =>
            new(IncomingStatus(reconciled.CoreStatus, reconciled.IpsDecision, reconciled.FollowUp), StatusSource.Gateway,
                null, reconciled.OccurredAtUtc, Description: reconciled.ManualReviewReason ?? ReversalDescription(reconciled.Reversal)),
        _ => null
    };

    // Before the IPS decision the core outcome decides; after it, an accepted reply is final and a rejected one is final
    // unless a follow-up remains: an unknown credit is still being reconciled, and a credit to reverse or any other
    // unresolved case needs an operator.
    private static TransactionStatus IncomingStatus(CoreOutcome core, IncomingIpsDecision? decision, IncomingFollowUp followUp) =>
        (decision, followUp) switch
        {
            (_, IncomingFollowUp.ManualReviewRequired or IncomingFollowUp.ReversalRequired) => TransactionStatus.ManualReview,
            ({ Accepted: true }, _) => TransactionStatus.Accepted,
            ({ Accepted: false }, IncomingFollowUp.ReconciliationRequired) => TransactionStatus.CoreUnknown,
            ({ Accepted: false }, _) => TransactionStatus.Rejected,
            (null, _) => core switch
            {
                CoreOutcome.SubmissionStarted => TransactionStatus.Sending,
                CoreOutcome.Unknown => TransactionStatus.CoreUnknown,
                CoreOutcome.Accepted => TransactionStatus.Accepted,
                CoreOutcome.Rejected => TransactionStatus.Rejected,
                _ => TransactionStatus.Received
            }
        };

    private static string? ReversalDescription(ReversalDelivery reversal) => reversal switch
    {
        ReversalDelivery.Started => "Reversal requested from the core system.",
        _ => null
    };

    private static TransferStatusRow Row(Guid paymentId, TransactionDirection direction, Entry entry) => new()
    {
        PaymentTransactionId = paymentId,
        Direction = direction,
        MessageType = PaymentMessageTypes.Pacs008,
        Status = entry.Status,
        Source = entry.Source,
        Step = entry.Step,
        ReasonCode = Limit(entry.ReasonCode, TransferStatusRow.ReasonCodeLength),
        IpsInternalCode = entry.IpsInternalCode,
        Description = Limit(entry.Description, TransferStatusRow.DescriptionLength),
        AtUtc = entry.AtUtc
    };

    // Core reason codes and descriptions are external text; they are cut to the column rather than failing the save.
    private static string? Limit(string? text, int length) => text is { Length: > 0 } value && value.Length > length ? value[..length] : text;

    private sealed record Entry(
        TransactionStatus Status,
        StatusSource Source,
        ProcessingStep? Step,
        DateTimeOffset AtUtc,
        string? ReasonCode = null,
        int? IpsInternalCode = null,
        string? Description = null);
}
