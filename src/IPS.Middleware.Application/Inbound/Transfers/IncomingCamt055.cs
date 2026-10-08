using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Recalls;

namespace IPS.Middleware.Application.Inbound.Transfers;

// The verified content of a PISP's cancellation request (camt.055) for a payment initiation (pain.001) we received, or
// its request for the status of an earlier cancellation request. The core system answers IPS itself.
public sealed record IncomingCamt055(
    string MessageId,
    string? AssignerBic,
    string? AssigneeBic,
    DateTimeOffset CreatedAt,
    string? PaymentCancellationId,
    string OriginalPaymentInformationId,
    IncomingCancelledGroup? OriginalGroup,
    string? CancellationId,
    string? OriginalInstructionId,
    string? OriginalEndToEndId,
    IncomingCancellationReason? Reason,
    IncomingCancelledInitiation Original) : IIncomingTransferContent
{
    string IIncomingTransferContent.Kind => PaymentMessageTypes.Camt055;

    string IIncomingTransferContent.Key => MessageId;

    // The debtor's agent holds the account the initiation would debit, so it must be us.
    string IIncomingTransferContent.ReceiverBic => Original.DebtorAgent.Bic!;

    CoreIdentifiers IIncomingTransferContent.Echo => new(null, MessageId, null);

    public IncomingCamt055 Frozen() => Original.Creditor is { Address: { } address }
        ? this with { Original = Original with { Creditor = Original.Creditor with { Address = RecallQuote.Frozen(address) } } }
        : this;
}

public sealed record IncomingCancelledGroup(string MessageId, string MessageNameId, DateTimeOffset? CreatedAt);

public sealed record IncomingCancellationReason(string? OriginatorName, string? OriginatorBic, string? ReasonCode, string? AdditionalInformation);

// The cancelled initiation as the request quotes it (OrgnlTxRef).
public sealed record IncomingCancelledInitiation(
    string? Currency,
    decimal? Amount,
    DateOnly? RequestedExecutionDate,
    string? ServiceLevelCode,
    string? LocalInstrumentCode,
    string? CategoryPurposeCode,
    string? RemittanceUnstructured,
    RecallAgentInput DebtorAgent,
    RecallAgentInput? CreditorAgent,
    IncomingCancelledCreditor? Creditor,
    string? CreditorAccount);

public sealed record IncomingCancelledCreditor(string? Name, string? Identifier, RecallAddressInput? Address);
