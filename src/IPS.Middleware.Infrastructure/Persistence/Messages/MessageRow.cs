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
