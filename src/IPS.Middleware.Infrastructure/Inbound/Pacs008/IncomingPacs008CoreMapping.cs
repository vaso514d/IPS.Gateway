using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.MiidleWear.Contracts.Pacs008;

namespace IPS.Middleware.Infrastructure.Inbound.Pacs008;

public static class IncomingPacs008CoreMapping
{
    public static Pacs008InstantPaymentRequestDto ToContract(IncomingPacs008 incoming)
    {
        var payment = incoming.Payment;
        return new()
        {
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
                    new Pacs008StructuredRemittanceDto
                    {
                        ReferenceType = reference.ReferenceType,
                        ReferenceIssuer = reference.ReferenceIssuer,
                        Reference = reference.Reference,
                        AdditionalInformation = reference.AdditionalInformation
                    }).ToArray())
            }
        };
    }

    private static Pacs008DebtorRequestDto? Debtor(Pacs008DebtorInput? value) => value is null ? null : new()
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

    private static Pacs008CreditorRequestDto? Creditor(Pacs008CreditorInput? value) => value is null ? null : new()
    {
        Type = value.Type,
        Name = value.Name,
        Identifier = value.Identifier,
        ParticipantBic = value.ParticipantBic,
        IndirectParticipantBic = value.IndirectParticipantBic,
        Account = value.Account,
        Address = Address(value.Address)
    };

    private static Pacs008UltimatePartyRequestDto? Ultimate(Pacs008UltimatePartyInput? value) => value is null ? null : new()
    {
        Type = value.Type,
        Name = value.Name,
        Identifier = value.Identifier
    };

    private static Pacs008PostalAddressDto? Address(Pacs008PostalAddressInput? value) => value is null ? null : new()
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
