using System.Text.Json.Serialization;

namespace IPS.MiidleWear.Contracts.Camt056;

/// <summary>
/// JSON REST contract for camt.056.001.11 (Request for Recall of an Instant Credit Transfer), sent by the original
/// debtor participant. Carries the client-supplied rows of the NBG field profile "IPS_camt.056 - V1" (37 rows; the
/// numbers in the remarks are that table's row numbers). IPS enforces a single TxInf, so the DTO is flat.
/// <para>
/// Rows fixed by Annex D (§3.2.4) are not on the contract — the gateway always fills them: #2 Assgnr (participant
/// BIC), #3 Assgne (IPS BIC), #7 OrgnlMsgNmId ("pacs.008.001.12"), #12 CxlRsnInf/Orgtr AnyBIC (= DbtrAgt, the
/// participant), #15 ClrSys "IPS", #16 SvcLvl, #17 LclInstrm, #19 CdtrRefInf/Tp/CdOrPrtry/Cd "SCOR".
/// </para>
/// <para>
/// The same shape is delivered to the core system at <see cref="Camt056RestApiRoutes.Receive"/> for an incoming recall of a
/// pacs.008 this bank received: every field is filled from the received message as IPS forwarded it, the creditor agent is
/// this participant, and <see cref="ClientReference"/> is empty.
/// </para>
/// </summary>
public sealed record Camt056RecallRequestDto
{
    /// <summary>
    /// REQUIRED. The core system's unique id for this transaction — unique across every message type. The idempotency
    /// key: a repeated request with the same value returns the existing transaction instead of sending again.
    /// Empty on a delivered (incoming) camt.056, whose idempotency key is <see cref="Id"/>.
    /// </summary>
    [JsonPropertyName("ClientReference")]
    public string? ClientReference { get; init; }

    /// <summary>Assgnmt/Id (#1, Max35Text): this recall message's id. Also the AppHdr BizMsgIdr.</summary>
    [JsonPropertyName("Id")]
    public string? Id { get; init; }

    /// <summary>Assgnmt/CreDtTm (#4). Omit to stamp "now" at XML-build time.</summary>
    [JsonPropertyName("CreDtTm")]
    public DateTimeOffset? CreDtTm { get; init; }

    /// <summary>TxInf/CxlId (#5, Max35Text): recall identification.</summary>
    [JsonPropertyName("RecallId")]
    public string? RecallId { get; init; }

    /// <summary>TxInf/OrgnlGrpInf/OrgnlMsgId (#6): GrpHdr/MsgId of the recalled pacs.008.</summary>
    [JsonPropertyName("OriginalMessageId")]
    public string? OriginalMessageId { get; init; }

    /// <summary>TxInf/OrgnlEndToEndId (#8).</summary>
    [JsonPropertyName("OriginalEndToEndId")]
    public string? OriginalEndToEndId { get; init; }

    /// <summary>TxInf/OrgnlTxId (#9).</summary>
    [JsonPropertyName("OriginalTransactionId")]
    public string? OriginalTransactionId { get; init; }

    /// <summary>TxInf/OrgnlIntrBkSttlmAmt/@Ccy (#10).</summary>
    [JsonPropertyName("OriginalCurrency")]
    public string? OriginalCurrency { get; init; }

    /// <summary>TxInf/OrgnlIntrBkSttlmAmt (#10).</summary>
    [JsonPropertyName("OriginalAmount")]
    public decimal? OriginalAmount { get; init; }

    /// <summary>TxInf/OrgnlIntrBkSttlmDt (#11): not in the future, not older than the scheme's recall window.</summary>
    [JsonPropertyName("OriginalSettlementDate")]
    public DateOnly? OriginalSettlementDate { get; init; }

    /// <summary>TxInf/CxlRsnInf/Rsn/Cd (#13): recall reason (Annex D §3.5.6 Recall Reasons, e.g. DUPL, FRAD, TECH, CUST, AC03, AM09).</summary>
    [JsonPropertyName("ReasonCode")]
    public string? ReasonCode { get; init; }

    /// <summary>TxInf/OrgnlTxRef (#14, #18-37): the recalled pacs.008 as it was sent.</summary>
    [JsonPropertyName("OriginalTransaction")]
    public Camt056OriginalTransactionReferenceDto? OriginalTransaction { get; init; }
}

/// <summary>TxInf/OrgnlTxRef (#14, #18-37).</summary>
public sealed record Camt056OriginalTransactionReferenceDto
{
    /// <summary>OrgnlTxRef/IntrBkSttlmDt (#14).</summary>
    [JsonPropertyName("SettlementDate")]
    public DateOnly? SettlementDate { get; init; }

    /// <summary>OrgnlTxRef/RmtInf (#18-21).</summary>
    [JsonPropertyName("RemittanceInformation")]
    public Camt056RemittanceInformationDto? RemittanceInformation { get; init; }

    /// <summary>OrgnlTxRef/UltmtDbtr/Pty (#22-23).</summary>
    [JsonPropertyName("UltimateDebtor")]
    public Camt056UltimatePartyDto? UltimateDebtor { get; init; }

    /// <summary>OrgnlTxRef/Dbtr/Pty (#24-26) and DbtrAcct/Id/IBAN (#27).</summary>
    [JsonPropertyName("Debtor")]
    public Camt056PartyDto? Debtor { get; init; }

    /// <summary>OrgnlTxRef/DbtrAgt/FinInstnId (#28-29). Must be this participant (Annex D §3.2.4.f).</summary>
    [JsonPropertyName("DebtorAgent")]
    public Camt056AgentDto? DebtorAgent { get; init; }

    /// <summary>OrgnlTxRef/CdtrAgt/FinInstnId (#30-31).</summary>
    [JsonPropertyName("CreditorAgent")]
    public Camt056AgentDto? CreditorAgent { get; init; }

    /// <summary>OrgnlTxRef/Cdtr/Pty (#32-34) and CdtrAcct/Id/IBAN (#35).</summary>
    [JsonPropertyName("Creditor")]
    public Camt056PartyDto? Creditor { get; init; }

    /// <summary>OrgnlTxRef/UltmtCdtr/Pty (#36-37).</summary>
    [JsonPropertyName("UltimateCreditor")]
    public Camt056UltimatePartyDto? UltimateCreditor { get; init; }
}

/// <summary>DbtrAgt / CdtrAgt: FinInstnId/BICFI and optional Nm (Max140Text).</summary>
public sealed record Camt056AgentDto
{
    [JsonPropertyName("BICFI")]
    public string? Bicfi { get; init; }

    [JsonPropertyName("Name")]
    public string? Name { get; init; }
}

/// <summary>Dbtr / Cdtr: Pty/Nm, Pty/PstlAdr, Pty/Id and the party's IBAN account (DbtrAcct / CdtrAcct).</summary>
public sealed record Camt056PartyDto
{
    /// <summary>Pty/Nm (Max140Text).</summary>
    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    /// <summary>Pty/PstlAdr (PostalAddress27).</summary>
    [JsonPropertyName("PostalAddress")]
    public Camt056PostalAddressDto? PostalAddress { get; init; }

    /// <summary>0 = legal entity (Pty/Id/OrgId/Othr/Id), 1 = individual (Pty/Id/PrvtId/Othr/Id).</summary>
    [JsonPropertyName("Type")]
    public int? Type { get; init; }

    /// <summary>Pty/Id/OrgId|PrvtId/Othr/Id (Max256Text), chosen by <see cref="Type"/>.</summary>
    [JsonPropertyName("Id")]
    public string? Id { get; init; }

    /// <summary>DbtrAcct / CdtrAcct Id/IBAN.</summary>
    [JsonPropertyName("Account")]
    public string? Account { get; init; }
}

/// <summary>UltmtDbtr / UltmtCdtr: Pty/Nm and Pty/Id only.</summary>
public sealed record Camt056UltimatePartyDto
{
    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    /// <summary>0 = legal entity (OrgId/Othr/Id), 1 = individual (PrvtId/Othr/Id).</summary>
    [JsonPropertyName("Type")]
    public int? Type { get; init; }

    [JsonPropertyName("Id")]
    public string? Id { get; init; }
}

/// <summary>Pty/PstlAdr — the PostalAddress27 elements a Georgian address needs, in XSD order.</summary>
public sealed record Camt056PostalAddressDto
{
    /// <summary>StrtNm (Max140Text).</summary>
    [JsonPropertyName("StreetName")]
    public string? StreetName { get; init; }

    /// <summary>BldgNb (Max16Text).</summary>
    [JsonPropertyName("BuildingNumber")]
    public string? BuildingNumber { get; init; }

    /// <summary>PstCd (Max16Text).</summary>
    [JsonPropertyName("PostCode")]
    public string? PostCode { get; init; }

    /// <summary>TwnNm (Max140Text).</summary>
    [JsonPropertyName("TownName")]
    public string? TownName { get; init; }

    /// <summary>CtrySubDvsn (Max35Text).</summary>
    [JsonPropertyName("CountrySubDivision")]
    public string? CountrySubDivision { get; init; }

    /// <summary>Ctry (ISO 3166-1 alpha-2).</summary>
    [JsonPropertyName("Country")]
    public string? Country { get; init; }

    /// <summary>AdrLine (up to 7 × Max70Text).</summary>
    [JsonPropertyName("AddressLines")]
    public IReadOnlyList<string>? AddressLines { get; init; }
}

/// <summary>OrgnlTxRef/RmtInf: Ustrd (Max140Text) and one Strd/CdtrRefInf (Annex D: a single Structured occurrence).</summary>
public sealed record Camt056RemittanceInformationDto
{
    /// <summary>RmtInf/Ustrd (#18).</summary>
    [JsonPropertyName("Unstructured")]
    public string? Unstructured { get; init; }

    /// <summary>RmtInf/Strd/CdtrRefInf (#19-21); Tp/CdOrPrtry/Cd is always "SCOR".</summary>
    [JsonPropertyName("CreditorReference")]
    public Camt056CreditorReferenceDto? CreditorReference { get; init; }
}

/// <summary>RmtInf/Strd/CdtrRefInf: Tp/Issr (#20) and Ref (#21).</summary>
public sealed record Camt056CreditorReferenceDto
{
    [JsonPropertyName("Issuer")]
    public string? Issuer { get; init; }

    [JsonPropertyName("Reference")]
    public string? Reference { get; init; }
}

public static class Camt056RestApiRoutes
{
    /// <summary>Implemented by the gateway; called by external clients to send a camt.056 recall request to IPS.</summary>
    public const string Send = "/api/ips/camt056/send";

    /// <summary>Implemented by the core system; called by the gateway for an incoming camt.056 recall request.</summary>
    public const string Receive = "/api/ips/camt056/receive";
}
