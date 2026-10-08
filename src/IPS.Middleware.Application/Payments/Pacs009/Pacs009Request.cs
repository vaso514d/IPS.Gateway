namespace IPS.Middleware.Application.Payments.Pacs009;

// A pacs.009 request as the caller supplied it. Every field is optional here; validation reports what is missing.
public sealed record Pacs009Request : IOutgoingPaymentRequest
{
    public string? ClientReference { get; init; }
    public string? Id { get; init; }
    public Pacs009AgentInput? DebtorAgent { get; init; }
    public Pacs009AgentInput? CreditorAgent { get; init; }
    public string? InstructionId { get; init; }
    public string? EndToEndId { get; init; }
    public string? TransactionId { get; init; }
    public Guid? Uetr { get; init; }
    public int? InstructionPriority { get; init; }
    public string? TransactionTypeCode { get; init; }
    public int? RtgsPriority { get; init; }
    public TimeOnly? FromTime { get; init; }
    public TimeOnly? RejectTime { get; init; }
    public DateOnly? ValueDate { get; init; }
    public string? Currency { get; init; }
    public decimal? Amount { get; init; }
    public Pacs009CodeInput? CategoryPurpose { get; init; }
    public string? Purpose { get; init; }
    public string? AdditionalPurpose { get; init; }
    public string? DebtorAccount { get; init; }
    public string? CreditorAccount { get; init; }
}

public sealed record Pacs009AgentInput
{
    public string? Bic { get; init; }
    public string? ClearingSystemMemberId { get; init; }
}

// A code (Type 0, or no type) or a proprietary text (Type 1).
public sealed record Pacs009CodeInput
{
    public int? Type { get; init; }
    public string? Value { get; init; }
}
