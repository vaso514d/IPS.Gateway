using System.Text.Json.Serialization;
using IPS.MiidleWear.Contracts.Pacs008;

namespace IPS.MiidleWear.Contracts.Pacs009;

/// <summary>
/// JSON REST contract for pacs.009 (FI-to-FI credit transfer) requests.
/// Reuses Pacs008AgentDto/Pacs008CodeChoiceDto since agent/code-choice shapes are identical.
/// </summary>
public sealed record Pacs009PaymentRequestDto
{
    /// <summary>
    /// REQUIRED. The core system's unique id for this transaction — unique across every message type. The idempotency
    /// key: a repeated request with the same value returns the existing transaction instead of sending again.
    /// </summary>
    [JsonPropertyName("ClientReference")]
    public string? ClientReference { get; init; }

    /// <summary>GrpHdr/MsgId: the message id. On send it is chosen by the caller and must be unused.</summary>
    [JsonPropertyName("Id")]
    public string? Id { get; init; }

    /// <summary>The debtor financial institution by BICFI (DbtrAgt, or Dbtr when the message has no debtor agent).</summary>
    [JsonPropertyName("DebtorAgent")]
    public Pacs008AgentDto? DebtorAgent { get; init; }

    /// <summary>The creditor financial institution by BICFI (CdtrAgt, or Cdtr when the message has no creditor agent).</summary>
    [JsonPropertyName("CreditorAgent")]
    public Pacs008AgentDto? CreditorAgent { get; init; }

    /// <summary>PmtId/InstrId: the instruction id, when given.</summary>
    [JsonPropertyName("InstrId")]
    public string? InstrId { get; init; }

    /// <summary>PmtId/EndToEndId: the end-to-end id. On delivery it is also the Idempotency-Key and the payment-status reference.</summary>
    [JsonPropertyName("EndToEndId")]
    public string? EndToEndId { get; init; }

    /// <summary>PmtId/TxId: the transaction id. On send it is chosen by the caller and must be unused.</summary>
    [JsonPropertyName("TxId")]
    public string? TxId { get; init; }

    /// <summary>PmtId/UETR of a received message, when given. Not in the IPS v1 profile: accepted on send and not sent.</summary>
    [JsonPropertyName("UETR")]
    public Guid? Uetr { get; init; }

    /// <summary>PmtTpInf/InstrPrty of a received message: 1 = HIGH, 0 = NORM, when given. Not in the IPS v1 profile: on send 0 or 1 is accepted and not sent.</summary>
    [JsonPropertyName("InstructionPriority")]
    public int? InstructionPriority { get; init; }

    /// <summary>Transaction type code kept from the source contract (at most 35 characters). Not in the IPS v1 profile: not sent, and empty on delivery.</summary>
    [JsonPropertyName("TTC")]
    public string? Ttc { get; init; }

    /// <summary>RTGS priority kept from the source contract (0 or 1). Not in the IPS v1 profile: not sent, and empty on delivery.</summary>
    [JsonPropertyName("RTGSPriority")]
    public int? RtgsPriority { get; init; }

    /// <summary>Earliest settlement time kept from the source contract; must be before RejectTime. Not in the IPS v1 profile: not sent, and empty on delivery.</summary>
    [JsonPropertyName("FromTime")]
    public TimeOnly? FromTime { get; init; }

    /// <summary>Latest settlement time kept from the source contract. Not in the IPS v1 profile: not sent, and empty on delivery.</summary>
    [JsonPropertyName("RejectTime")]
    public TimeOnly? RejectTime { get; init; }

    /// <summary>IntrBkSttlmDt: the interbank settlement date.</summary>
    [JsonPropertyName("ValueDate")]
    public DateOnly? ValueDate { get; init; }

    /// <summary>IntrBkSttlmAmt/@Ccy: the settlement currency.</summary>
    [JsonPropertyName("Currency")]
    public string? Currency { get; init; }

    /// <summary>IntrBkSttlmAmt: the settlement amount.</summary>
    [JsonPropertyName("Amount")]
    public decimal? Amount { get; init; }

    /// <summary>GrpHdr/PmtTpInf/CtgyPurp/Cd (NBG pacs.009 - V1 #10). Exposed on the wire because the NBG profile
    /// allows it — a deliberate deviation from SWIFT's FiToFiCreditTransferTransaction, which hides this field.</summary>
    [JsonPropertyName("CategoryPurpose")]
    public Pacs008CodeChoiceDto? CategoryPurpose { get; init; }

    /// <summary>Flat purpose text, matching FiToFiCreditTransferTransaction.Purpose (not a Type/Value choice).</summary>
    [JsonPropertyName("Purpose")]
    public string? Purpose { get; init; }

    /// <summary>CdtTrfTxInf/RmtInf/Ustrd (NBG pacs.009 - V1 #27, Max140Text). Exposed on the wire because the NBG
    /// profile allows it — a deliberate deviation from SWIFT's FiToFiCreditTransferTransaction, which hides this field.</summary>
    [JsonPropertyName("AddPurpose")]
    public string? AddPurpose { get; init; }

    /// <summary>Mirrors FiToFiCreditTransferTransaction's Debtor field (FiAgentAccount: an object wrapping Account).</summary>
    [JsonPropertyName("Debtor")]
    public Pacs009AccountDto? Debtor { get; init; }

    /// <summary>Mirrors FiToFiCreditTransferTransaction's Creditor field (FiAgentAccount: an object wrapping Account).</summary>
    [JsonPropertyName("Creditor")]
    public Pacs009AccountDto? Creditor { get; init; }
}

/// <summary>Mirrors SWIFT.Middleware.Domain.XmlEntities.Pacs009.FiAgentAccount — a single Account field.</summary>
public sealed record Pacs009AccountDto
{
    /// <summary>DbtrAcct or CdtrAcct: the IBAN (on delivery Othr/Id when the message has no IBAN).</summary>
    [JsonPropertyName("Account")]
    public string? Account { get; init; }
}
