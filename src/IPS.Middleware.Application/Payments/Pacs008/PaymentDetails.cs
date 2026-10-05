namespace IPS.Middleware.Application.Payments.Pacs008;

public enum PaymentPartyKind
{
    Organisation = 0,
    Individual = 1
}

public enum PaymentAccountKind
{
    Iban,
    Treasury
}

public enum PaymentPriority
{
    Normal,
    High
}

public sealed class PaymentParty
{
    public PaymentParty(PaymentPartyKind kind, string name, string? identifier, string? billIdentifier, PaymentAddress? address)
    {
        Kind = kind;
        Name = name;
        Identifier = identifier;
        BillIdentifier = billIdentifier;
        Address = address;
    }

    public PaymentPartyKind Kind { get; init; }
    public string Name { get; init; }
    public string? Identifier { get; init; }
    public string? BillIdentifier { get; init; }
    public PaymentAddress? Address { get; init; }
}

public sealed class PaymentAccount
{
    public PaymentAccount(string value, PaymentAccountKind kind)
    {
        Value = value;
        Kind = kind;
    }

    public string Value { get; init; }
    public PaymentAccountKind Kind { get; init; }
}

public sealed class PaymentAgent
{
    public PaymentAgent(string bic, string? indirectParticipant)
    {
        Bic = bic;
        IndirectParticipant = indirectParticipant;
    }

    public string Bic { get; init; }
    public string? IndirectParticipant { get; init; }
}

public sealed class PaymentAddress
{
    public PaymentAddress(
        string? streetName,
        string? buildingNumber,
        string? postCode,
        string? townName,
        string? countrySubdivision,
        string? country,
        string? addressLines)
    {
        StreetName = streetName;
        BuildingNumber = buildingNumber;
        PostCode = postCode;
        TownName = townName;
        CountrySubdivision = countrySubdivision;
        Country = country;
        AddressLines = addressLines;
    }

    public string? StreetName { get; init; }
    public string? BuildingNumber { get; init; }
    public string? PostCode { get; init; }
    public string? TownName { get; init; }
    public string? CountrySubdivision { get; init; }
    public string? Country { get; init; }
    public string? AddressLines { get; init; }
}

public sealed class PaymentInitiation
{
    [System.Text.Json.Serialization.JsonConstructor]
    public PaymentInitiation(string? channelCode, IReadOnlyList<string> geolocation)
    {
        ChannelCode = channelCode;
        Geolocation = geolocation;
    }

    public string? ChannelCode { get; init; }
    public IReadOnlyList<string> Geolocation { get; init; }

    public PaymentInitiation(PaymentInitiation original)
    {
        ChannelCode = original.ChannelCode;
        Geolocation = original.Geolocation;
    }
}

public sealed class PaymentInitiationChannel
{
    [System.Text.Json.Serialization.JsonConstructor]
    public PaymentInitiationChannel(string channelCode, IReadOnlyList<string> instrumentCodes, string? electronicAddress)
    {
        ChannelCode = channelCode;
        InstrumentCodes = instrumentCodes;
        ElectronicAddress = electronicAddress;
    }

    public string ChannelCode { get; init; }
    public IReadOnlyList<string> InstrumentCodes { get; init; }
    public string? ElectronicAddress { get; init; }

    public PaymentInitiationChannel(PaymentInitiationChannel original)
    {
        ChannelCode = original.ChannelCode;
        InstrumentCodes = original.InstrumentCodes;
        ElectronicAddress = original.ElectronicAddress;
    }
}

public sealed class PaymentRemittance
{
    [System.Text.Json.Serialization.JsonConstructor]
    public PaymentRemittance(string? unstructured, IReadOnlyList<PaymentRemittanceReference> structured)
    {
        Unstructured = unstructured;
        Structured = structured;
    }

    public string? Unstructured { get; init; }
    public IReadOnlyList<PaymentRemittanceReference> Structured { get; init; }

    public PaymentRemittance(PaymentRemittance original)
    {
        Unstructured = original.Unstructured;
        Structured = original.Structured;
    }
}

public sealed class PaymentRemittanceReference
{
    public PaymentRemittanceReference(string type, string reference, string? issuer, string? additionalInformation)
    {
        Type = type;
        Reference = reference;
        Issuer = issuer;
        AdditionalInformation = additionalInformation;
    }

    public string Type { get; init; }
    public string Reference { get; init; }
    public string? Issuer { get; init; }
    public string? AdditionalInformation { get; init; }
}
