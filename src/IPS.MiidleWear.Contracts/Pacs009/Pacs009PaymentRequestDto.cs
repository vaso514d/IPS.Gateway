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

    [JsonPropertyName("Id")]
    public string? Id { get; init; }

    [JsonPropertyName("DebtorAgent")]
    public Pacs008AgentDto? DebtorAgent { get; init; }

    [JsonPropertyName("CreditorAgent")]
    public Pacs008AgentDto? CreditorAgent { get; init; }

    [JsonPropertyName("InstrId")]
    public string? InstrId { get; init; }

    [JsonPropertyName("EndToEndId")]
    public string? EndToEndId { get; init; }

    [JsonPropertyName("TxId")]
    public string? TxId { get; init; }

    [JsonPropertyName("UETR")]
    public Guid? Uetr { get; init; }

    [JsonPropertyName("InstructionPriority")]
    public int? InstructionPriority { get; init; }

    [JsonPropertyName("TTC")]
    public string? Ttc { get; init; }

    [JsonPropertyName("RTGSPriority")]
    public int? RtgsPriority { get; init; }

    [JsonPropertyName("FromTime")]
    public TimeOnly? FromTime { get; init; }

    [JsonPropertyName("RejectTime")]
    public TimeOnly? RejectTime { get; init; }

    [JsonPropertyName("ValueDate")]
    public DateOnly? ValueDate { get; init; }

    [JsonPropertyName("Currency")]
    public string? Currency { get; init; }

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
    [JsonPropertyName("Account")]
    public string? Account { get; init; }
}
