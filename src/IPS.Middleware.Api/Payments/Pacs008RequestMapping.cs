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
            PaymentInitiation = payment.PaymentInitiation is not { } initiation ? null : new()
            { ChannelCode = initiation.ChannelCode, Geolocation = initiation.Geolocation },
            InitiationChannelInstrument = payment.InitiationChannelInstrument is not { } instrument ? null : new()
            { ChannelCode = instrument.ChannelCode, InstrumentCodes = instrument.InstrumentCodes, ElectronicAddress = instrument.ElectronicAddress },
            Remittance = payment.Remittance is not { } remittance ? null : new()
            {
                Unstructured = remittance.Unstructured,
                Structured = remittance.Structured is null ? null : Array.AsReadOnly(remittance.Structured.Select(reference =>
                    reference is null ? null! : new Pacs008StructuredRemittanceInput
                    {
                        ReferenceType = reference.ReferenceType,
                        ReferenceIssuer = reference.ReferenceIssuer,
                        Reference = reference.Reference,
                        AdditionalInformation = reference.AdditionalInformation
                    }).ToArray())
            }
        };
    }

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
