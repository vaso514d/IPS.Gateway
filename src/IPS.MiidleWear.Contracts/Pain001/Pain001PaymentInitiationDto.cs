using System.Text.Json.Serialization;
using IPS.MiidleWear.Contracts.Pacs008;

namespace IPS.MiidleWear.Contracts.Pain001;

/// <summary>
/// pain.001.001.12 (Customer Credit Transfer Initiation) as JSON — one shape for both directions, like the other
/// message types:
/// <list type="bullet">
/// <item>incoming (now): a PISP asks this bank, the originator participant (the payer's bank), to make a payment
/// (Annex D §3.2.11). The gateway posts it to the core system at <see cref="Pain001RestApiRoutes.Receive"/>;</item>
/// <item>outgoing (later): this bank as a PISP.</item>
/// </list>
/// The parties, address and remittance use the pacs.008 Bank/Core shapes on purpose: the core system answers an
/// accepted initiation with a pacs.008 and can copy them over — endToEndId = "PSP-" + <see cref="PaymentInformationId"/>,
/// acceptanceDateTime = <see cref="CreationDateTime"/> (Annex D §3.2.1.6). A refusal is a pain.002 that quotes
/// <see cref="MessageId"/> and <see cref="PaymentInformationId"/>. Fields follow Annex D §8.1.11 (p. 180–185).
/// </summary>
public sealed record Pain001PaymentInitiationDto
{
    /// <summary>Outgoing only: the core system's unique id (idempotency key). Empty on an incoming pain.001.</summary>
    [JsonPropertyName("clientReference")]
    public string? ClientReference { get; init; }

    /// <summary>GrpHdr/MsgId (1.1): quoted by a pain.002 refusal as OriginalMessageId.</summary>
    [JsonPropertyName("messageId")]
    public string? MessageId { get; init; }

    /// <summary>GrpHdr/CreDtTm (1.2): the acceptanceDateTime of the pacs.008 that pays this initiation.</summary>
    [JsonPropertyName("creationDateTime")]
    public DateTimeOffset? CreationDateTime { get; init; }

    /// <summary>GrpHdr/InitgPty (1.5–1.10): the PISP that initiated the payment.</summary>
    [JsonPropertyName("initiatingParty")]
    public Pain001InitiatingPartyDto? InitiatingParty { get; init; }

    /// <summary>PmtInf/PmtInfId (2.2): the pacs.008 endToEndId is "PSP-" + this; a pain.002 quotes it as OriginalPaymentInformationId.</summary>
    [JsonPropertyName("paymentInformationId")]
    public string? PaymentInformationId { get; init; }

    /// <summary>PmtTpInf/SvcLvl/Cd (2.6).</summary>
    [JsonPropertyName("serviceLevelCode")]
    public string? ServiceLevelCode { get; init; }

    /// <summary>PmtTpInf/LclInstrm/Cd (2.8).</summary>
    [JsonPropertyName("localInstrumentCode")]
    public string? LocalInstrumentCode { get; init; }

    /// <summary>PmtTpInf/CtgyPurp/Cd (2.10).</summary>
    [JsonPropertyName("categoryPurposeCode")]
    public string? CategoryPurposeCode { get; init; }

    /// <summary>ReqdExctnDt (2.11).</summary>
    [JsonPropertyName("requestedExecutionDate")]
    public DateOnly? RequestedExecutionDate { get; init; }

    /// <summary>Dbtr (2.12–2.15), DbtrAcct/Id/IBAN (2.18) and DbtrAgt/FinInstnId/BICFI (2.21) — the payer; participantBic is this bank.</summary>
    [JsonPropertyName("debtor")]
    public Pacs008DebtorRequestDto? Debtor { get; init; }

    /// <summary>UltmtDbtr (2.23–2.25).</summary>
    [JsonPropertyName("ultimateDebtor")]
    public Pacs008UltimatePartyRequestDto? UltimateDebtor { get; init; }

    /// <summary>CdtTrfTxInf/PmtId/InstrId (2.29).</summary>
    [JsonPropertyName("instructionId")]
    public string? InstructionId { get; init; }

    /// <summary>CdtTrfTxInf/PmtId/EndToEndId (2.30): the PISP's own reference — not the pacs.008 endToEndId.</summary>
    [JsonPropertyName("endToEndId")]
    public string? EndToEndId { get; init; }

    /// <summary>Amt/InstdAmt (2.32).</summary>
    [JsonPropertyName("amount")]
    public decimal? Amount { get; init; }

    /// <summary>Amt/InstdAmt/@Ccy (2.32).</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    /// <summary>Cdtr (2.37–2.40), CdtrAcct/Id/IBAN (2.43) and CdtrAgt/FinInstnId/BICFI (2.35) — the payee and its bank.</summary>
    [JsonPropertyName("creditor")]
    public Pacs008CreditorRequestDto? Creditor { get; init; }

    /// <summary>UltmtCdtr (2.44–2.46).</summary>
    [JsonPropertyName("ultimateCreditor")]
    public Pacs008UltimatePartyRequestDto? UltimateCreditor { get; init; }

    /// <summary>Purp/Cd (2.47).</summary>
    [JsonPropertyName("purposeCode")]
    public string? PurposeCode { get; init; }

    /// <summary>RmtInf (2.48–2.55): Ustrd, and one Strd/CdtrRefInf with Tp/CdOrPrtry/Cd = SCOR.</summary>
    [JsonPropertyName("remittance")]
    public Pacs008RemittanceDto? Remittance { get; init; }
}

/// <summary>GrpHdr/InitgPty: Nm (1.7) and Id/OrgId/AnyBIC (1.10, "BIC or BEI").</summary>
public sealed record Pain001InitiatingPartyDto
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("bic")]
    public string? Bic { get; init; }
}

public static class Pain001RestApiRoutes
{
    /// <summary>Implemented by the core system; called by the gateway for an incoming pain.001 payment initiation.</summary>
    public const string Receive = "/api/ips/pain001/receive";
}
