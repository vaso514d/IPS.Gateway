namespace IPS.Middleware.Application.Payments.Camt056;

// A camt.056 request as the caller supplied it. Every field is optional here; validation reports what is missing.
public sealed record Camt056Request : IOutgoingPaymentRequest
{
    public string? ClientReference { get; init; }
    public string? Id { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public string? RecallId { get; init; }
    public string? OriginalMessageId { get; init; }
    public string? OriginalEndToEndId { get; init; }
    public string? OriginalTransactionId { get; init; }
    public string? OriginalCurrency { get; init; }
    public decimal? OriginalAmount { get; init; }
    public DateOnly? OriginalSettlementDate { get; init; }
    public string? ReasonCode { get; init; }
    public RecallOriginalInput? OriginalTransaction { get; init; }
}

// The recalled payment as the caller quotes it.
public sealed record RecallOriginalInput
{
    public DateOnly? SettlementDate { get; init; }
    public RecallRemittanceInput? Remittance { get; init; }
    public RecallUltimatePartyInput? UltimateDebtor { get; init; }
    public RecallPartyInput? Debtor { get; init; }
    public RecallAgentInput? DebtorAgent { get; init; }
    public RecallAgentInput? CreditorAgent { get; init; }
    public RecallPartyInput? Creditor { get; init; }
    public RecallUltimatePartyInput? UltimateCreditor { get; init; }
}

public sealed record RecallPartyInput
{
    public string? Name { get; init; }
    public RecallAddressInput? Address { get; init; }
    public int? Type { get; init; }
    public string? Identifier { get; init; }
    public string? Account { get; init; }
}

public sealed record RecallUltimatePartyInput
{
    public string? Name { get; init; }
    public int? Type { get; init; }
    public string? Identifier { get; init; }
}

public sealed record RecallAddressInput
{
    public string? StreetName { get; init; }
    public string? BuildingNumber { get; init; }
    public string? PostCode { get; init; }
    public string? TownName { get; init; }
    public string? CountrySubdivision { get; init; }
    public string? Country { get; init; }
    public IReadOnlyList<string>? AddressLines { get; init; }
}

public sealed record RecallAgentInput
{
    public string? Bic { get; init; }
    public string? Name { get; init; }
}

public sealed record RecallRemittanceInput
{
    public string? Unstructured { get; init; }
    public RecallCreditorReferenceInput? CreditorReference { get; init; }
}

public sealed record RecallCreditorReferenceInput
{
    public string? Issuer { get; init; }
    public string? Reference { get; init; }
}
