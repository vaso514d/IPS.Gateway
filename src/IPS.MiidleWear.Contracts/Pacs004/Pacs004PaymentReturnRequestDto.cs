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

    [JsonPropertyName("Id")]
    public string? Id { get; init; }

    [JsonPropertyName("Amount")]
    public decimal? Amount { get; init; }

    [JsonPropertyName("Currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("ValueDate")]
    public DateOnly? ValueDate { get; init; }

    /// <summary>Transaction Type Code, mirrors CreditTransferTransaction.TTC.</summary>
    [JsonPropertyName("TTC")]
    public string? Ttc { get; init; }

    [JsonPropertyName("InstructingAgent")]
    public string? InstructingAgent { get; init; }

    /// <summary>
    /// OPTIONAL. The indirect participant returning the funds (TxInf/OrgnlTxRef/CdtrAgt/FinInstnId/ClrSysMmbId/MmbId,
    /// Max35Text). Outgoing: must be in IpsStp:IndirectParticipants; empty = the return is our own. Incoming: as received.
    /// </summary>
    [JsonPropertyName("SenderClrSysMmbId")]
    public string? SenderClrSysMmbId { get; init; }

    [JsonPropertyName("InstructedAgent")]
    public string? InstructedAgent { get; init; }

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

    [JsonPropertyName("Original")]
    public Pacs004OriginalPaymentReferenceDto? Original { get; init; }
}

public sealed record Pacs004OriginalPaymentReferenceDto
{
    [JsonPropertyName("TransactionId")]
    public string? TransactionId { get; init; }

    [JsonPropertyName("InstructionId")]
    public string? InstructionId { get; init; }

    [JsonPropertyName("EndToEndId")]
    public string? EndToEndId { get; init; }

    [JsonPropertyName("UETR")]
    public Guid? Uetr { get; init; }

    [JsonPropertyName("ValueDate")]
    public DateOnly? ValueDate { get; init; }

    [JsonPropertyName("OriginalMessageId")]
    public string? OriginalMessageId { get; init; }

    [JsonPropertyName("OriginalMessageNameId")]
    public string? OriginalMessageNameId { get; init; }

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
