namespace IPS.Middleware.Application.Payments.Pacs008;

public sealed record Pacs008Request : IOutgoingPaymentRequest
{
    public string? ClientReference { get; init; }
    public string? InstructionId { get; init; }
    public string? EndToEndId { get; init; }
    public DateTimeOffset? CreationDateTime { get; init; }
    public DateTimeOffset? AcceptanceDateTime { get; init; }
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    public string? InstructionPriority { get; init; }
    public string? CategoryPurposeCode { get; init; }
    public Pacs008DebtorInput? Debtor { get; init; }
    public Pacs008CreditorInput? Creditor { get; init; }
    public Pacs008UltimatePartyInput? UltimateDebtor { get; init; }
    public Pacs008UltimatePartyInput? UltimateCreditor { get; init; }
    public Pacs008PaymentInitiationInput? PaymentInitiation { get; init; }
    public Pacs008InitiationChannelInstrumentInput? InitiationChannelInstrument { get; init; }
    public Pacs008RemittanceInput? Remittance { get; init; }
}

public abstract record Pacs008PartyInput
{
    public int? Type { get; init; }
    public string? Name { get; init; }
    public string? Identifier { get; init; }
}

public sealed record Pacs008DebtorInput : Pacs008PartyInput
{
    public string? ParticipantBic { get; init; }
    public string? BillIdentifier { get; init; }
    public Pacs008PostalAddressInput? Address { get; init; }
    public string? IndirectParticipantBic { get; init; }
    public string? Account { get; init; }
}

public sealed record Pacs008CreditorInput : Pacs008PartyInput
{
    public Pacs008PostalAddressInput? Address { get; init; }
    public string? ParticipantBic { get; init; }
    public string? IndirectParticipantBic { get; init; }
    public string? Account { get; init; }
}

public sealed record Pacs008UltimatePartyInput : Pacs008PartyInput;

public sealed record Pacs008PostalAddressInput
{
    public string? StreetName { get; init; }
    public string? BuildingNumber { get; init; }
    public string? PostCode { get; init; }
    public string? TownName { get; init; }
    public string? CountrySubdivision { get; init; }
    public string? Country { get; init; }
    public string? AddressLines { get; init; }
}

public sealed record Pacs008PaymentInitiationInput
{
    public string? ChannelCode { get; init; }
    public IReadOnlyList<string>? Geolocation { get; init; }
}

public sealed record Pacs008InitiationChannelInstrumentInput
{
    public string? ChannelCode { get; init; }
    public IReadOnlyList<string>? InstrumentCodes { get; init; }
    public string? ElectronicAddress { get; init; }
}

public sealed record Pacs008RemittanceInput
{
    public string? Unstructured { get; init; }
    public IReadOnlyList<Pacs008StructuredRemittanceInput>? Structured { get; init; }
}

public sealed record Pacs008StructuredRemittanceInput
{
    public string? ReferenceType { get; init; }
    public string? ReferenceIssuer { get; init; }
    public string? Reference { get; init; }
    public string? AdditionalInformation { get; init; }
}
