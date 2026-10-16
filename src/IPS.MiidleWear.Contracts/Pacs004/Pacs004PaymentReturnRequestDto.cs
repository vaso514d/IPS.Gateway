using System.Text.Json.Serialization;
using IPS.MiidleWear.Contracts.Pacs008;

namespace IPS.MiidleWear.Contracts.Pacs004;

/// <summary>
/// JSON REST contract for pacs.004 (payment return) requests. Field names mirror
/// SWIFT.Middleware.Domain.XmlEntities.Pacs004.ReturnPaymentsEntity (the core template) where the
/// underlying data is identical, and reuse Pacs008AgentDto/Pacs008PartyDto shapes since those
/// concepts (agent, party) are the same across pacs.004/008/009.
/// </summary>
public sealed record Pacs004PaymentReturnRequestDto
{
    /// <summary>
    /// REQUIRED. The core system's unique id for this transaction — unique across every message type. The idempotency
    /// key: a repeated request with the same value returns the existing transaction instead of sending again.
    /// </summary>
    [JsonPropertyName("ClientReference")]
    public string? ClientReference { get; init; }

    /// <summary>TxInf/RtrId, equal to GrpHdr/MsgId: the return id. On send it is chosen by the caller and must be unused; on delivery it is also the Idempotency-Key and the payment-status reference.</summary>
    [JsonPropertyName("Id")]
    public string? Id { get; init; }

    /// <summary>TxInf/RtrdIntrBkSttlmAmt: the returned amount.</summary>
    [JsonPropertyName("Amount")]
    public decimal? Amount { get; init; }

    /// <summary>RtrdIntrBkSttlmAmt/@Ccy: the currency of the returned amount.</summary>
    [JsonPropertyName("Currency")]
    public string? Currency { get; init; }

    /// <summary>IntrBkSttlmDt: the settlement date of the return.</summary>
    [JsonPropertyName("ValueDate")]
    public DateOnly? ValueDate { get; init; }

    /// <summary>Transaction Type Code, mirrors CreditTransferTransaction.TTC.</summary>
    [JsonPropertyName("TTC")]
    public string? Ttc { get; init; }

    /// <summary>BIC of the participant returning the funds (GrpHdr/InstgAgt; on delivery OrgnlTxRef/CdtrAgt when absent).</summary>
    [JsonPropertyName("InstructingAgent")]
    public string? InstructingAgent { get; init; }

    /// <summary>
    /// OPTIONAL. The indirect participant returning the funds (TxInf/OrgnlTxRef/CdtrAgt/FinInstnId/ClrSysMmbId/MmbId,
    /// Max35Text). Outgoing: must be in IpsStp:IndirectParticipants; empty = the return is our own. Incoming: as received.
    /// </summary>
    [JsonPropertyName("SenderClrSysMmbId")]
    public string? SenderClrSysMmbId { get; init; }

    /// <summary>BIC of the participant receiving the returned funds (GrpHdr/InstdAgt; on delivery OrgnlTxRef/DbtrAgt when absent).</summary>
    [JsonPropertyName("InstructedAgent")]
    public string? InstructedAgent { get; init; }

    /// <summary>RtrRsnInf/Rsn/Cd (or Prtry): the return reason. On send only FOCR is accepted.</summary>
    [JsonPropertyName("ReturnReasonCode")]
    public string? ReturnReasonCode { get; init; }

    /// <summary>Bank BIC that serviced the original debtor. For the core system's own reference only — not sent:
    /// OrgnlTxRef/DbtrAgt is <see cref="InstructedAgent"/>. Empty on a received return.</summary>
    [JsonPropertyName("DebtorSWIFT")]
    public string? DebtorSwift { get; init; }

    /// <summary>
    /// REQUIRED. The original payer, who gets the money back — OrgnlTxRef/Dbtr/Pty (Name → Nm, Type + Id → Id/OrgId|PrvtId/Othr/Id)
    /// and OrgnlTxRef/DbtrAcct/Id/IBAN (Account). Annex D §8.1.4 2.40-2.47.
    /// </summary>
    [JsonPropertyName("Debtor")]
    public Pacs008PartyDto? Debtor { get; init; }

    /// <summary>Bank BIC that serviced the original creditor. For the core system's own reference only — not sent:
    /// OrgnlTxRef/CdtrAgt is our BIC. Empty on a received return.</summary>
    [JsonPropertyName("CreditorSWIFT")]
    public string? CreditorSwift { get; init; }

    /// <summary>
    /// REQUIRED. The original payee, who returns the money — OrgnlTxRef/Cdtr/Pty and OrgnlTxRef/CdtrAcct/Id/IBAN.
    /// Annex D §8.1.4 2.56-2.63.
    /// </summary>
    [JsonPropertyName("Creditor")]
    public Pacs008PartyDto? Creditor { get; init; }

    /// <summary>Not sent (not in Annex D §8.1.4). Kept for the core system's own reference.</summary>
    [JsonPropertyName("OriginatorName")]
    public string? OriginatorName { get; init; }

    /// <summary>Not sent (not in Annex D §8.1.4). Kept for the core system's own reference.</summary>
    [JsonPropertyName("OriginatorAddress")]
    public string? OriginatorAddress { get; init; }

    /// <summary>Not sent: RtrRsnInf/AddtlInf is not in Annex D §8.1.4 (2.12-2.18 has Orgtr and Rsn/Cd only).</summary>
    [JsonPropertyName("AdditionalInfo")]
    public string? AdditionalInfo { get; init; }

    /// <summary>The original payment being returned (the Orgnl* elements and OrgnlTxRef).</summary>
    [JsonPropertyName("Original")]
    public Pacs004OriginalPaymentReferenceDto? Original { get; init; }
}

public sealed record Pacs004OriginalPaymentReferenceDto
{
    /// <summary>OrgnlTxId: the transaction id of the original payment.</summary>
    [JsonPropertyName("TransactionId")]
    public string? TransactionId { get; init; }

    /// <summary>OrgnlInstrId: the instruction id of the original payment, when given.</summary>
    [JsonPropertyName("InstructionId")]
    public string? InstructionId { get; init; }

    /// <summary>OrgnlEndToEndId: the end-to-end id of the original payment.</summary>
    [JsonPropertyName("EndToEndId")]
    public string? EndToEndId { get; init; }

    /// <summary>OrgnlUETR: the UETR of the original payment, when given.</summary>
    [JsonPropertyName("UETR")]
    public Guid? Uetr { get; init; }

    /// <summary>The settlement date of the original payment (OrgnlTxRef/IntrBkSttlmDt, or OrgnlIntrBkSttlmDt).</summary>
    [JsonPropertyName("ValueDate")]
    public DateOnly? ValueDate { get; init; }

    /// <summary>OrgnlGrpInf/OrgnlMsgId: the message id of the original payment, when given.</summary>
    [JsonPropertyName("OriginalMessageId")]
    public string? OriginalMessageId { get; init; }

    /// <summary>OrgnlGrpInf/OrgnlMsgNmId: the original message definition, for example pacs.008.001.12, when given.</summary>
    [JsonPropertyName("OriginalMessageNameId")]
    public string? OriginalMessageNameId { get; init; }

    /// <summary>OrgnlGrpInf/OrgnlCreDtTm: when the original message was created, when given.</summary>
    [JsonPropertyName("OriginalCreationDateTime")]
    public DateTimeOffset? OriginalCreationDateTime { get; init; }

    /// <summary>
    /// OPTIONAL. The original payment's amount — TxInf/OrgnlIntrBkSttlmAmt (Annex D 2.9, [1..1]). Empty → the returned
    /// Amount/Currency is sent (a full return, which FOCR is).
    /// </summary>
    [JsonPropertyName("Amount")]
    public decimal? Amount { get; init; }

    /// <summary>OPTIONAL. Currency of <see cref="Amount"/>; empty → the return's Currency.</summary>
    [JsonPropertyName("Currency")]
    public string? Currency { get; init; }
}
