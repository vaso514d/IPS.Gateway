using System.Text.Json.Serialization;
using IPS.MiidleWear.Contracts.Camt056;

namespace IPS.MiidleWear.Contracts.Camt029;

/// <summary>
/// JSON REST contract for camt.029.001.13 (Resolution of Investigation) — the creditor participant's negative answer
/// to a camt.056 recall (the positive answer is a pacs.004). Carries the client-supplied rows of the NBG field profile
/// "IPS_camt.029 - V1" (40 rows; the numbers in the remarks are that table's row numbers).
/// <para>
/// Rows fixed by Annex D (§3.2.5, §8.1.6) are not on the contract — the gateway always fills them: #2 Assgnr
/// (participant BIC), #3 Assgne (IPS BIC), #5 Sts/Conf "RJCR", #8 OrgnlMsgNmId "camt.056.001.11", #11 TxCxlSts
/// "RJCR", #12 CxlStsRsnInf/Orgtr AnyBIC (= CdtrAgt, the participant), #17 SttlmMtd "CLRG", #18 ClrSys "IPS",
/// #19 SvcLvl, #20 LclInstrm, #22 CdtrRefInf/Tp/CdOrPrtry/Cd "SCOR".
/// </para>
/// </summary>
public sealed record Camt029ResolutionOfInvestigationDto
{
    /// <summary>
    /// REQUIRED. The core system's unique id for this transaction — unique across every message type. The idempotency
    /// key: a repeated request with the same value returns the existing transaction instead of sending again.
    /// </summary>
    [JsonPropertyName("ClientReference")]
    public string? ClientReference { get; init; }

    /// <summary>Assgnmt/Id (#1, Max35Text): this message's id. Also the AppHdr BizMsgIdr.</summary>
    [JsonPropertyName("Id")]
    public string? Id { get; init; }

    /// <summary>Assgnmt/CreDtTm (#4). Omit to stamp "now" at XML-build time.</summary>
    [JsonPropertyName("CreDtTm")]
    public DateTimeOffset? CreDtTm { get; init; }

    /// <summary>CxlDtls/TxInfAndSts/CxlStsId (#6, Max35Text).</summary>
    [JsonPropertyName("CancellationStatusId")]
    public string? CancellationStatusId { get; init; }

    /// <summary>TxInfAndSts/OrgnlGrpInf/OrgnlMsgId (#7): Assgnmt/Id of the camt.056 being answered.</summary>
    [JsonPropertyName("OriginalMessageId")]
    public string? OriginalMessageId { get; init; }

    /// <summary>TxInfAndSts/OrgnlEndToEndId (#9): EndToEndId of the recalled pacs.008.</summary>
    [JsonPropertyName("OriginalEndToEndId")]
    public string? OriginalEndToEndId { get; init; }

    /// <summary>TxInfAndSts/OrgnlTxId (#10): TxId of the recalled pacs.008.</summary>
    [JsonPropertyName("OriginalTransactionId")]
    public string? OriginalTransactionId { get; init; }

    /// <summary>TxInfAndSts/CxlStsRsnInf/Rsn/Cd (#13): reason for refusing the recall (Annex D §3.5.6 Recall Nack Reasons, e.g. NOAS, NOOR, ARDT, CUST, AC04, AM04, LEGL).</summary>
    [JsonPropertyName("ReasonCode")]
    public string? ReasonCode { get; init; }

    /// <summary>TxInfAndSts/CxlStsRsnInf/AddtlInf (#14, Max105Text).</summary>
    [JsonPropertyName("AdditionalInformation")]
    public string? AdditionalInformation { get; init; }

    /// <summary>TxInfAndSts/OrgnlTxRef (#15-16, #21-40): the recalled pacs.008 as it was received.</summary>
    [JsonPropertyName("OriginalTransaction")]
    public Camt029OriginalTransactionReferenceDto? OriginalTransaction { get; init; }
}

/// <summary>
/// TxInfAndSts/OrgnlTxRef (#15-16, #21-40). Same shape as the camt.056 one plus IntrBkSttlmAmt (#15), which only
/// the camt.029 profile carries inside OrgnlTxRef.
/// </summary>
public sealed record Camt029OriginalTransactionReferenceDto
{
    /// <summary>OrgnlTxRef/IntrBkSttlmAmt/@Ccy (#15).</summary>
    [JsonPropertyName("Currency")]
    public string? Currency { get; init; }

    /// <summary>OrgnlTxRef/IntrBkSttlmAmt (#15).</summary>
    [JsonPropertyName("Amount")]
    public decimal? Amount { get; init; }

    /// <summary>OrgnlTxRef/IntrBkSttlmDt (#16).</summary>
    [JsonPropertyName("SettlementDate")]
    public DateOnly? SettlementDate { get; init; }

    /// <summary>OrgnlTxRef/RmtInf (#21-24).</summary>
    [JsonPropertyName("RemittanceInformation")]
    public Camt056RemittanceInformationDto? RemittanceInformation { get; init; }

    /// <summary>OrgnlTxRef/UltmtDbtr/Pty (#25-26).</summary>
    [JsonPropertyName("UltimateDebtor")]
    public Camt056UltimatePartyDto? UltimateDebtor { get; init; }

    /// <summary>OrgnlTxRef/Dbtr/Pty (#27-29) and DbtrAcct/Id/IBAN (#30).</summary>
    [JsonPropertyName("Debtor")]
    public Camt056PartyDto? Debtor { get; init; }

    /// <summary>OrgnlTxRef/DbtrAgt/FinInstnId (#31-32).</summary>
    [JsonPropertyName("DebtorAgent")]
    public Camt056AgentDto? DebtorAgent { get; init; }

    /// <summary>OrgnlTxRef/CdtrAgt/FinInstnId (#33-34). Must be this participant (Annex D §3.2.5.g).</summary>
    [JsonPropertyName("CreditorAgent")]
    public Camt056AgentDto? CreditorAgent { get; init; }

    /// <summary>OrgnlTxRef/Cdtr/Pty (#35-37) and CdtrAcct/Id/IBAN (#38).</summary>
    [JsonPropertyName("Creditor")]
    public Camt056PartyDto? Creditor { get; init; }

    /// <summary>OrgnlTxRef/UltmtCdtr/Pty (#39-40).</summary>
    [JsonPropertyName("UltimateCreditor")]
    public Camt056UltimatePartyDto? UltimateCreditor { get; init; }
}

public static class Camt029RestApiRoutes
{
    /// <summary>Implemented by the gateway; called by external clients to send a camt.029 negative answer to a recall.</summary>
    public const string Send = "/api/ips/camt029/send";
}
