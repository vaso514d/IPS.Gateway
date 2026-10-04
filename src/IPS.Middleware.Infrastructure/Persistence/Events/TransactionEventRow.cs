using System.Text.Json;
using System.Text.Json.Serialization;
using IPS.Middleware.Domain;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Infrastructure.Persistence.Events;

internal sealed class TransactionEventRow
{
    public Guid EventId { get; set; }
    public Guid TransactionId { get; set; }
    public string AggregateKind { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string PayloadJson { get; set; } = string.Empty;

    public static TransactionEventRow From(AggregateRoot aggregate, DomainEvent occurrence) => new()
    {
        EventId = occurrence.EventId,
        TransactionId = occurrence.AggregateId,
        AggregateKind = EventRegistry.Kind(aggregate),
        Sequence = occurrence.Sequence,
        Name = EventRegistry.Name(occurrence),
        SchemaVersion = 1,
        OccurredAtUtc = occurrence.OccurredAtUtc,
        PayloadJson = JsonSerializer.Serialize(occurrence, occurrence.GetType(), EventRegistry.JsonOptions)
    };
}

internal static class EventRegistry
{
    internal const string OutgoingPaymentKind = "outgoing-payment";
    internal const string IncomingPaymentKind = "incoming-payment";

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    internal static string Kind(AggregateRoot aggregate) => aggregate switch
    {
        OutgoingPayment => OutgoingPaymentKind,
        IncomingPayment => IncomingPaymentKind,
        _ => throw new InvalidOperationException("Unregistered aggregate.")
    };

    // Event names start with their aggregate's prefix; SQL checks the prefix against the owning aggregate kind.
    internal static string Name(DomainEvent occurrence) => occurrence switch
    {
        IncomingReconciliationRecorded => "incoming-payment.reconciliation-recorded",
        IncomingPaymentRegistered => "incoming-payment.registered",
        IncomingProcessingRecorded record => record.Operation switch
        {
            IncomingProcessingOperation.SubmissionStarted => "incoming-payment.submission-started",
            IncomingProcessingOperation.CoreOutcomeRecorded => "incoming-payment.core-outcome-recorded",
            IncomingProcessingOperation.IpsDecisionRecorded => "incoming-payment.ips-decision-recorded",
            IncomingProcessingOperation.OutcomeConflictObserved => "incoming-payment.outcome-conflict-observed",
            IncomingProcessingOperation.OutcomeObserved => "incoming-payment.outcome-observed",
            _ => throw new InvalidOperationException("Unregistered incoming operation.")
        },
        PaymentReceived => "payment.received",
        PaymentProcessingObserved => "payment.processing-observed",
        PaymentProcessingFailed => "payment.processing-failed",
        PaymentOutcomeObserved { Conflicting: true } => "payment.outcome-conflict-observed",
        PaymentOutcomeObserved => "payment.outcome-observed",
        PaymentStateChanged change => change.Operation switch
        {
            PaymentOperation.BeginSending => "payment.sending-started",
            PaymentOperation.Accept => "payment.accepted",
            PaymentOperation.Reject => "payment.rejected",
            PaymentOperation.RetryConnection => "payment.connection-retry-scheduled",
            PaymentOperation.NotSent => "payment.not-sent",
            PaymentOperation.OutcomeUnknown => "payment.outcome-unknown",
            PaymentOperation.BeginInvestigation => "payment.investigation-started",
            PaymentOperation.BeginResending => "payment.resending-started",
            PaymentOperation.RequireManualReview => "payment.manual-review-required",
            PaymentOperation.ResolveManually => "payment.manually-resolved",
            _ => throw new InvalidOperationException("Unregistered payment operation.")
        },
        _ => throw new InvalidOperationException("Unregistered domain event.")
    };
}
