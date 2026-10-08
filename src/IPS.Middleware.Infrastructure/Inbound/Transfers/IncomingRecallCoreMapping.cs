using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Application.Payments.Recalls;
using IPS.MiidleWear.Contracts.Camt029;
using IPS.MiidleWear.Contracts.Camt056;

namespace IPS.Middleware.Infrastructure.Inbound.Transfers;

// The core-facing JSON of a received camt.056 and of a received camt.029, in the shapes the core system already sends.
internal static class IncomingRecallCoreMapping
{
    // No client reference is set: the core keys on the recall's message id.
    internal static Camt056RecallRequestDto ToContract(IncomingCamt056 recall) => new()
    {
        Id = recall.MessageId,
        CreDtTm = recall.CreatedAt,
        RecallId = recall.RecallId,
        OriginalMessageId = recall.OriginalMessageId,
        OriginalEndToEndId = recall.OriginalEndToEndId,
        OriginalTransactionId = recall.OriginalTransactionId,
        OriginalCurrency = recall.OriginalCurrency,
        OriginalAmount = recall.OriginalAmount,
        OriginalSettlementDate = recall.OriginalSettlementDate,
        ReasonCode = recall.ReasonCode,
        OriginalTransaction = new Camt056OriginalTransactionReferenceDto
        {
            SettlementDate = recall.Original.SettlementDate,
            RemittanceInformation = Remittance(recall.Original.Remittance),
            UltimateDebtor = Ultimate(recall.Original.UltimateDebtor),
            Debtor = Party(recall.Original.Debtor),
            DebtorAgent = Agent(recall.Original.DebtorAgent),
            CreditorAgent = Agent(recall.Original.CreditorAgent),
            Creditor = Party(recall.Original.Creditor),
            UltimateCreditor = Ultimate(recall.Original.UltimateCreditor)
        }
    };

    // The client reference is that of our refused recall.
    internal static Camt029ResolutionOfInvestigationDto ToContract(IncomingCamt029 refusal) => new()
    {
        ClientReference = refusal.RecallClientReference,
        Id = refusal.MessageId,
        CreDtTm = refusal.CreatedAt,
        CancellationStatusId = refusal.CancellationStatusId,
        OriginalMessageId = refusal.OriginalMessageId,
        OriginalEndToEndId = refusal.OriginalEndToEndId,
        OriginalTransactionId = refusal.OriginalTransactionId,
        ReasonCode = refusal.ReasonCode,
        AdditionalInformation = refusal.AdditionalInformation,
        OriginalTransaction = new Camt029OriginalTransactionReferenceDto
        {
            Currency = refusal.Original.Currency,
            Amount = refusal.Original.Amount,
            SettlementDate = refusal.Original.SettlementDate,
            RemittanceInformation = Remittance(refusal.Original.Remittance),
            UltimateDebtor = Ultimate(refusal.Original.UltimateDebtor),
            Debtor = Party(refusal.Original.Debtor),
            DebtorAgent = Agent(refusal.Original.DebtorAgent),
            CreditorAgent = Agent(refusal.Original.CreditorAgent),
            Creditor = Party(refusal.Original.Creditor),
            UltimateCreditor = Ultimate(refusal.Original.UltimateCreditor)
        }
    };

    internal static Camt056PostalAddressDto? Address(RecallAddressInput? address) => address is null ? null : new()
    {
        StreetName = address.StreetName,
        BuildingNumber = address.BuildingNumber,
        PostCode = address.PostCode,
        TownName = address.TownName,
        CountrySubDivision = address.CountrySubdivision,
        Country = address.Country,
        AddressLines = address.AddressLines
    };

    private static Camt056RemittanceInformationDto? Remittance(RecallRemittanceInput? remittance) => remittance is null ? null : new()
    {
        Unstructured = remittance.Unstructured,
        CreditorReference = remittance.CreditorReference is { } reference
            ? new Camt056CreditorReferenceDto { Issuer = reference.Issuer, Reference = reference.Reference }
            : null
    };

    private static Camt056PartyDto? Party(RecallPartyInput? party) => party is null ? null : new()
    {
        Name = party.Name,
        PostalAddress = Address(party.Address),
        Type = party.Type,
        Id = party.Identifier,
        Account = party.Account
    };

    private static Camt056UltimatePartyDto? Ultimate(RecallUltimatePartyInput? party) => party is null ? null : new()
    {
        Name = party.Name,
        Type = party.Type,
        Id = party.Identifier
    };

    private static Camt056AgentDto? Agent(RecallAgentInput? agent) => agent is null ? null : new()
    {
        Bicfi = agent.Bic,
        Name = agent.Name
    };
}
