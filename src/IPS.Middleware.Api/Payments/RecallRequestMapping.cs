using IPS.Middleware.Application.Payments.Camt029;
using IPS.Middleware.Application.Payments.Camt056;
using IPS.Middleware.Application.Payments.Recalls;
using IPS.MiidleWear.Contracts.Camt029;
using IPS.MiidleWear.Contracts.Camt056;

namespace IPS.Middleware.Api.Payments;

// Maps the camt.056 and camt.029 DTOs to requests; both carry the same recalled-payment block.
internal static class RecallRequestMapping
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

    public static Camt029Request Map(Camt029ResolutionOfInvestigationDto answer) => new()
    {
        ClientReference = answer.ClientReference,
        Id = answer.Id,
        CreatedAt = answer.CreDtTm,
        CancellationStatusId = answer.CancellationStatusId,
        OriginalMessageId = answer.OriginalMessageId,
        OriginalEndToEndId = answer.OriginalEndToEndId,
        OriginalTransactionId = answer.OriginalTransactionId,
        ReasonCode = answer.ReasonCode,
        AdditionalInformation = answer.AdditionalInformation,
        OriginalTransaction = answer.OriginalTransaction is { } original
            ? new Camt029OriginalInput
            {
                Currency = original.Currency,
                Amount = original.Amount,
                SettlementDate = original.SettlementDate,
                Remittance = Remittance(original.RemittanceInformation),
                UltimateDebtor = Ultimate(original.UltimateDebtor),
                Debtor = Party(original.Debtor),
                DebtorAgent = Agent(original.DebtorAgent),
                CreditorAgent = Agent(original.CreditorAgent),
                Creditor = Party(original.Creditor),
                UltimateCreditor = Ultimate(original.UltimateCreditor)
            }
            : null
    };

    private static RecallOriginalInput? Original(Camt056OriginalTransactionReferenceDto? value) => value is null ? null
        : Original(value.SettlementDate, value.RemittanceInformation, value.UltimateDebtor, value.Debtor,
            value.DebtorAgent, value.CreditorAgent, value.Creditor, value.UltimateCreditor);

    private static RecallOriginalInput Original(
        DateOnly? settlementDate,
        Camt056RemittanceInformationDto? remittance,
        Camt056UltimatePartyDto? ultimateDebtor,
        Camt056PartyDto? debtor,
        Camt056AgentDto? debtorAgent,
        Camt056AgentDto? creditorAgent,
        Camt056PartyDto? creditor,
        Camt056UltimatePartyDto? ultimateCreditor) => new()
        {
            SettlementDate = settlementDate,
            Remittance = Remittance(remittance),
            UltimateDebtor = Ultimate(ultimateDebtor),
            Debtor = Party(debtor),
            DebtorAgent = Agent(debtorAgent),
            CreditorAgent = Agent(creditorAgent),
            Creditor = Party(creditor),
            UltimateCreditor = Ultimate(ultimateCreditor)
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
