using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Infrastructure.Persistence.Messages;

// The columns every message table shares (specification 020): one row per payment aggregate, both directions, with its
// current status. A queryable projection of the aggregate and its request; never read back by the workflows.
internal abstract class MessageRow
{
    internal const int ReferenceLength = 35;
    internal const int DescriptionLength = 2000;

    public Guid Id { get; set; }
    public TransactionDirection Direction { get; set; }
    public TransactionStatus Status { get; set; }
    public DateTimeOffset StatusAtUtc { get; set; }
    public string? ReasonCode { get; set; }
    public string? Description { get; set; }
    public string? ClientReference { get; set; }
    public string? CoreReference { get; set; }
    public string? MessageId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

// pacs.008, with the original gateway's Pacs008Payments columns that the gateway's model carries.
internal sealed class Pacs008PaymentRow : MessageRow
{
    public string? InstructionId { get; set; }
    public string? EndToEndId { get; set; }
    public string? TransactionId { get; set; }
    public Guid? Uetr { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateOnly? ValueDate { get; set; }
    public DateTimeOffset? AcceptanceDateTime { get; set; }
    public string? DebtorName { get; set; }
    public string? DebtorAccount { get; set; }
    public string? DebtorIdentification { get; set; }
    public string? DebtorAgentBic { get; set; }
    public string? CreditorName { get; set; }
    public string? CreditorAccount { get; set; }
    public string? CreditorIdentification { get; set; }
    public string? CreditorAgentBic { get; set; }
    public string? CategoryPurpose { get; set; }
    public string? RemittanceInformation { get; set; }
}

// pacs.009, with the original gateway's Pacs009Payments columns that the gateway's model carries.
internal sealed class Pacs009PaymentRow : MessageRow
{
    public string? InstructionId { get; set; }
    public string? EndToEndId { get; set; }
    public string? TransactionId { get; set; }
    public Guid? Uetr { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateOnly? ValueDate { get; set; }
    public string? DebtorAgentBic { get; set; }
    public string? DebtorAccount { get; set; }
    public string? CreditorAgentBic { get; set; }
    public string? CreditorAccount { get; set; }
    public string? CategoryPurpose { get; set; }
    public string? Purpose { get; set; }
}

// pacs.004, with the original gateway's Pacs004Payments columns and the returned parties.
internal sealed class Pacs004PaymentRow : MessageRow
{
    public string? ReturnId { get; set; }
    public string? ReturnReasonCode { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateOnly? ValueDate { get; set; }
    public string? InstructedAgentBic { get; set; }
    public string? DebtorName { get; set; }
    public string? DebtorAccount { get; set; }
    public string? CreditorName { get; set; }
    public string? CreditorAccount { get; set; }
    public string? OriginalMessageId { get; set; }
    public string? OriginalMessageNameId { get; set; }
    public string? OriginalInstructionId { get; set; }
    public string? OriginalEndToEndId { get; set; }
    public string? OriginalTransactionId { get; set; }
    public Guid? OriginalUetr { get; set; }
    public string? AdditionalInformation { get; set; }
}

// pain.001, received from a payment initiation service provider.
internal sealed class Pain001InitiationRow : MessageRow
{
    public string? PaymentInformationId { get; set; }
    public string? InstructionId { get; set; }
    public string? EndToEndId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateOnly? RequestedExecutionDate { get; set; }
    public string? InitiatingPartyBic { get; set; }
    public string? DebtorName { get; set; }
    public string? DebtorAccount { get; set; }
    public string? DebtorAgentBic { get; set; }
    public string? CreditorName { get; set; }
    public string? CreditorAccount { get; set; }
    public string? CreditorAgentBic { get; set; }
    public string? CategoryPurpose { get; set; }
    public string? Purpose { get; set; }
    public string? RemittanceInformation { get; set; }
}

// camt.056, a recall in either direction.
internal sealed class Camt056RecallRow : MessageRow
{
    public string? RecallId { get; set; }
    public string? OriginalMessageId { get; set; }
    public string? OriginalEndToEndId { get; set; }
    public string? OriginalTransactionId { get; set; }
    public decimal? OriginalAmount { get; set; }
    public string? OriginalCurrency { get; set; }
    public DateOnly? OriginalSettlementDate { get; set; }
    public string? RecallReasonCode { get; set; }
    public string? DebtorName { get; set; }
    public string? DebtorAccount { get; set; }
    public string? DebtorAgentBic { get; set; }
    public string? CreditorName { get; set; }
    public string? CreditorAccount { get; set; }
    public string? CreditorAgentBic { get; set; }
}

// camt.055, a payment initiation service provider's cancellation request.
internal sealed class Camt055CancellationRow : MessageRow
{
    public string? CancellationId { get; set; }
    public string? PaymentCancellationId { get; set; }
    public string? AssignerBic { get; set; }
    public string? AssigneeBic { get; set; }
    public string? OriginalMessageId { get; set; }
    public string? OriginalPaymentInformationId { get; set; }
    public string? OriginalInstructionId { get; set; }
    public string? OriginalEndToEndId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateOnly? RequestedExecutionDate { get; set; }
    public string? CancellationReasonCode { get; set; }
    public string? OriginatorBic { get; set; }
    public string? CreditorName { get; set; }
    public string? CreditorAccount { get; set; }
}

// camt.029, a resolution of investigation (a recall refusal) in either direction.
internal sealed class Camt029ResolutionRow : MessageRow
{
    public string? CancellationStatusId { get; set; }
    public string? OriginalMessageId { get; set; }
    public string? OriginalEndToEndId { get; set; }
    public string? OriginalTransactionId { get; set; }
    public string? ResolutionReasonCode { get; set; }
    public string? AdditionalInformation { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateOnly? SettlementDate { get; set; }
}

// pain.002, the status report the bank sends for a received pain.001.
internal sealed class Pain002StatusReportRow : MessageRow
{
    public string? OriginalMessageId { get; set; }
    public string? OriginalPaymentInformationId { get; set; }
    public string? ReportReasonCode { get; set; }
    public string? AdditionalInformation { get; set; }
    public string? OriginatorName { get; set; }
}

// pacs.002: outgoing, the gateway's reply to a received pacs.008; incoming, a status report IPS sends on its own. Keyed by
// the inbound journal entry it answers or arrived as, so it has no payment aggregate.
internal sealed class Pacs002StatusReportRow : MessageRow
{
    public string? OriginalMessageId { get; set; }
    public string? OriginalEndToEndId { get; set; }
    public string? OriginalTransactionId { get; set; }
    public string? TransactionStatusCode { get; set; }
    public string? ReportReasonCode { get; set; }
    public string? AdditionalInformation { get; set; }
}
