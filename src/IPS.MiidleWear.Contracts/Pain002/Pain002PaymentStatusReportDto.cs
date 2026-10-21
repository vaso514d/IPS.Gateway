using System.Text.Json.Serialization;

namespace IPS.MiidleWear.Contracts.Pain002;

/// <summary>
/// JSON REST contract for pain.002.001.14 (Customer Payment Status Report) — this bank, as the originator participant
/// (the payer's bank that received a pain.001 from a PISP), refuses the payment initiation (Annex D §3.2.11–§3.2.12,
/// Figure 23 steps 4a–6a). IPS answers with a pain.002 (5a) and forwards the refusal to the PISP (6a). Accepting a
/// pain.001 is not a pain.002: the payment is sent as a pacs.008 whose EndToEndId is "PSP-" + the pain.001 PmtInfId.
/// <para>
/// Fields follow Annex D §8.1.12 (p. 186–188); sizes follow pain.002.001.14.xsd. The gateway fills the rest itself:
/// GrpHdr/DbtrAgt (this participant), OrgnlMsgNmId (the pain.001 schema), GrpSts and PmtInfSts ("RJCT").
/// </para>
/// </summary>
public sealed record Pain002PaymentStatusReportDto
{
    /// <summary>
    /// REQUIRED. The core system's unique id for this transaction — unique across every message type. The idempotency
    /// key: a repeated request with the same value returns the existing transaction instead of sending again.
    /// </summary>
    [JsonPropertyName("ClientReference")]
    public string? ClientReference { get; init; }

    /// <summary>REQUIRED. GrpHdr/MsgId (1.1, Max35Text): this message's id. Also the AppHdr BizMsgIdr.</summary>
    [JsonPropertyName("Id")]
    public string? Id { get; init; }

    /// <summary>GrpHdr/CreDtTm (1.2). Omit to stamp "now" at XML-build time.</summary>
    [JsonPropertyName("CreDtTm")]
    public DateTimeOffset? CreDtTm { get; init; }

    /// <summary>REQUIRED. OrgnlGrpInfAndSts/OrgnlMsgId (2.2, Max35Text): GrpHdr/MsgId of the refused pain.001.</summary>
    [JsonPropertyName("OriginalMessageId")]
    public string? OriginalMessageId { get; init; }

    /// <summary>REQUIRED. OrgnlPmtInfAndSts/OrgnlPmtInfId (3.2, Max35Text): PmtInfId of the refused pain.001.</summary>
    [JsonPropertyName("OriginalPaymentInformationId")]
    public string? OriginalPaymentInformationId { get; init; }

    /// <summary>REQUIRED. StsRsnInf/Rsn/Cd (2.7, 3.8): why the payment is refused — an ISO external status reason code (1–4 characters, e.g. AC04, AM04, CUST).</summary>
    [JsonPropertyName("ReasonCode")]
    public string? ReasonCode { get; init; }

    /// <summary>StsRsnInf/AddtlInf (2.8, 3.9, Max105Text): free-text explanation.</summary>
    [JsonPropertyName("AdditionalInformation")]
    public string? AdditionalInformation { get; init; }

    /// <summary>OrgnlPmtInfAndSts/StsRsnInf/Orgtr/Nm (3.6, Max140Text): who refused (the debtor or this bank).</summary>
    [JsonPropertyName("OriginatorName")]
    public string? OriginatorName { get; init; }
}

public static class Pain002RestApiRoutes
{
    /// <summary>Implemented by the gateway; called by the core system to refuse a received pain.001 payment initiation.</summary>
    public const string Send = "/api/ips/pain002/send";
}
