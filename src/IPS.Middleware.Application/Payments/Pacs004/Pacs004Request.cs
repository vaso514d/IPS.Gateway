namespace IPS.Middleware.Application.Payments.Pacs004;

// A pacs.004 request as the caller supplied it. Every field is optional here; validation reports what is missing.
public sealed record Pacs004Request : IOutgoingPaymentRequest
{
    public string? ClientReference { get; init; }
    public string? Id { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public DateOnly? ValueDate { get; init; }
    public string? TransactionTypeCode { get; init; }
    public string? InstructingAgent { get; init; }
    public string? SenderIndirectParticipant { get; init; }
    public string? InstructedAgent { get; init; }
    public string? ReturnReasonCode { get; init; }
    public string? DebtorSwift { get; init; }
    public string? CreditorSwift { get; init; }
    public Pacs004PartyInput? Debtor { get; init; }
    public Pacs004PartyInput? Creditor { get; init; }
    public string? OriginatorName { get; init; }
    public string? OriginatorAddress { get; init; }
    public string? AdditionalInfo { get; init; }
    public Pacs004OriginalInput? Original { get; init; }
}

// A party of the original payment: the debtor gets the money back, the creditor returns it.
public sealed record Pacs004PartyInput
{
    public int? Type { get; init; }
    public string? Name { get; init; }
    public string? Identifier { get; init; }
    public string? Account { get; init; }
}

public sealed record Pacs004OriginalInput
{
    public string? TransactionId { get; init; }
    public string? InstructionId { get; init; }
    public string? EndToEndId { get; init; }
    public Guid? Uetr { get; init; }
    public DateOnly? ValueDate { get; init; }
    public string? OriginalMessageId { get; init; }
    public string? OriginalMessageNameId { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
}
