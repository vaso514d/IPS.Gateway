using IPS.Middleware.Application.Payments.Camt056;
using IPS.MiidleWear.Contracts.Camt056;

namespace IPS.Middleware.Api.Payments;

internal static class Camt056RequestMapping
{
    public static Camt056Request Map(Camt056RecallRequestDto recall) => new()
    {
        ClientReference = recall.ClientReference,
        Id = recall.Id,
        CreatedAt = recall.CreDtTm,
        RecallId = recall.RecallId,
        OriginalMessageId = recall.OriginalMessageId,
        OriginalEndToEndId = recall.OriginalEndToEndId,
        OriginalTransactionId = recall.OriginalTransactionId,
        OriginalCurrency = recall.OriginalCurrency,
        OriginalAmount = recall.OriginalAmount,
        OriginalSettlementDate = recall.OriginalSettlementDate,
        ReasonCode = recall.ReasonCode,
        OriginalTransaction = Original(recall.OriginalTransaction)
    };

    private static RecallOriginalInput? Original(Camt056OriginalTransactionReferenceDto? value) => value is null ? null : new()
    {
        SettlementDate = value.SettlementDate,
        Remittance = Remittance(value.RemittanceInformation),
        UltimateDebtor = Ultimate(value.UltimateDebtor),
        Debtor = Party(value.Debtor),
        DebtorAgent = Agent(value.DebtorAgent),
        CreditorAgent = Agent(value.CreditorAgent),
        Creditor = Party(value.Creditor),
        UltimateCreditor = Ultimate(value.UltimateCreditor)
    };

    private static RecallRemittanceInput? Remittance(Camt056RemittanceInformationDto? value) => value is null ? null : new()
    {
        Unstructured = value.Unstructured,
        CreditorReference = value.CreditorReference is { } reference
            ? new RecallCreditorReferenceInput { Issuer = reference.Issuer, Reference = reference.Reference }
            : null
    };

    private static RecallPartyInput? Party(Camt056PartyDto? value) => value is null ? null : new()
    {
        Name = value.Name,
        Address = Address(value.PostalAddress),
        Type = value.Type,
        Identifier = value.Id,
        Account = value.Account
    };

    private static RecallUltimatePartyInput? Ultimate(Camt056UltimatePartyDto? value) => value is null ? null : new()
    {
        Name = value.Name,
        Type = value.Type,
        Identifier = value.Id
    };

    private static RecallAddressInput? Address(Camt056PostalAddressDto? value) => value is null ? null : new()
    {
        StreetName = value.StreetName,
        BuildingNumber = value.BuildingNumber,
        PostCode = value.PostCode,
        TownName = value.TownName,
        CountrySubdivision = value.CountrySubDivision,
        Country = value.Country,
        AddressLines = value.AddressLines
    };

    private static RecallAgentInput? Agent(Camt056AgentDto? value) => value is null ? null : new()
    {
        Bic = value.Bicfi,
        Name = value.Name
    };
}
