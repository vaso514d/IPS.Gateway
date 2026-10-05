using IPS.Middleware.Application.Payments.Pacs008;
using IPS.MiidleWear.Contracts.Pacs008;

namespace IPS.Middleware.Api.Payments;

internal static class Pacs008RequestMapping
{
    public static Pacs008Request Map(Pacs008InstantPaymentRequestDto payment)
    {
        return new()
        {
            ClientReference = payment.ClientReference,
            InstructionId = payment.InstructionId,
            EndToEndId = payment.EndToEndId,
            CreationDateTime = payment.CreationDateTime,
            AcceptanceDateTime = payment.AcceptanceDateTime,
            Amount = payment.Amount,
            Currency = payment.Currency,
            InstructionPriority = payment.InstructionPriority,
            CategoryPurposeCode = payment.CategoryPurposeCode,
            Debtor = Debtor(payment.Debtor),
            Creditor = Creditor(payment.Creditor),
            UltimateDebtor = Ultimate(payment.UltimateDebtor),
            UltimateCreditor = Ultimate(payment.UltimateCreditor),
            PaymentInitiation = Initiation(payment.PaymentInitiation),
            InitiationChannelInstrument = Instrument(payment.InitiationChannelInstrument),
            Remittance = Remittance(payment.Remittance)
        };
    }

    private static Pacs008PaymentInitiationInput? Initiation(Pacs008PaymentInitiationDto? value) => value is null ? null : new()
    {
        ChannelCode = value.ChannelCode,
        Geolocation = value.Geolocation
    };

    private static Pacs008InitiationChannelInstrumentInput? Instrument(Pacs008InitiationChannelInstrumentDto? value) => value is null ? null : new()
    {
        ChannelCode = value.ChannelCode,
        InstrumentCodes = value.InstrumentCodes,
        ElectronicAddress = value.ElectronicAddress
    };

    // A null list element is kept so validation can report it at its position.
    private static Pacs008RemittanceInput? Remittance(Pacs008RemittanceDto? value) => value is null ? null : new()
    {
        Unstructured = value.Unstructured,
        Structured = value.Structured is null ? null : Array.AsReadOnly(value.Structured.Select(StructuredReference).ToArray())
    };

    private static Pacs008StructuredRemittanceInput StructuredReference(Pacs008StructuredRemittanceDto? value) => value is null ? null! : new()
    {
        ReferenceType = value.ReferenceType,
        ReferenceIssuer = value.ReferenceIssuer,
        Reference = value.Reference,
        AdditionalInformation = value.AdditionalInformation
    };

    private static Pacs008DebtorInput? Debtor(Pacs008DebtorRequestDto? value) => value is null ? null : new()
    {
        Type = value.Type,
        Name = value.Name,
        Identifier = value.Identifier,
        BillIdentifier = value.BillIdentifier,
        ParticipantBic = value.ParticipantBic,
        IndirectParticipantBic = value.IndirectParticipantBic,
        Account = value.Account,
        Address = Address(value.Address)
    };

    private static Pacs008CreditorInput? Creditor(Pacs008CreditorRequestDto? value) => value is null ? null : new()
    {
        Type = value.Type,
        Name = value.Name,
        Identifier = value.Identifier,
        ParticipantBic = value.ParticipantBic,
        IndirectParticipantBic = value.IndirectParticipantBic,
        Account = value.Account,
        Address = Address(value.Address)
    };

    private static Pacs008UltimatePartyInput? Ultimate(Pacs008UltimatePartyRequestDto? value) => value is null ? null : new()
    {
        Type = value.Type,
        Name = value.Name,
        Identifier = value.Identifier
    };

    private static Pacs008PostalAddressInput? Address(Pacs008PostalAddressDto? value) => value is null ? null : new()
    {
        StreetName = value.StreetName,
        BuildingNumber = value.BuildingNumber,
        PostCode = value.PostCode,
        TownName = value.TownName,
        CountrySubdivision = value.CountrySubdivision,
        Country = value.Country,
        AddressLines = value.AddressLines
    };
}
