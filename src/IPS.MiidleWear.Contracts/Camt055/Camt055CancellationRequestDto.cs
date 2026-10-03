using System.Text.Json;
using System.Text.Json.Serialization;

namespace IPS.MiidleWear.Contracts.Camt055;

/// <summary>
/// JSON REST contract for camt.055.001.012 (Customer Payment Cancellation Request), modeled after
/// Annex D §7.1.13. Used either as a Request for Cancellation (RfC) of a previously sent pain.001 or
/// pain.013 message, or as a Request for Status Update (RFSU) on an already-submitted cancellation.
/// A single TxInf occurrence is enforced by IPS, so this DTO is flattened the same way the other
/// contract-only models in this solution are.
/// </summary>
public sealed record Camt055CancellationRequestDto
{
    // ---- Group Header (Assgnmt) ----

    [JsonPropertyName("MsgId")]
    public string? MsgId { get; init; }

    [JsonPropertyName("AssignerBIC")]
    public string? AssignerBic { get; init; }

    [JsonPropertyName("AssigneeBIC")]
    public string? AssigneeBic { get; init; }

    [JsonPropertyName("CreDtTm")]
    public DateTime? CreDtTm { get; init; }

    // ---- Original Payment Information and Cancellation (OrgnlPmtInfAndCxl) ----

    [JsonPropertyName("PaymentCancellationId")]
    public string? PaymentCancellationId { get; init; }

    [JsonPropertyName("OriginalPaymentInformationId")]
    public string? OriginalPaymentInformationId { get; init; }

    [JsonPropertyName("OriginalGroupInformation")]
    public Camt055OriginalGroupInformationDto? OriginalGroupInformation { get; init; }

    // ---- Transaction Information (TxInf) ----

    [JsonPropertyName("CancellationId")]
    public string? CancellationId { get; init; }

    [JsonPropertyName("OriginalInstructionId")]
    public string? OriginalInstructionId { get; init; }

    [JsonPropertyName("OriginalEndToEndId")]
    public string? OriginalEndToEndId { get; init; }

    [JsonPropertyName("CancellationReason")]
    public Camt055CancellationReasonDto? CancellationReason { get; init; }

    [JsonPropertyName("OriginalTransaction")]
    public Camt055OriginalTransactionReferenceDto? OriginalTransaction { get; init; }

    // NOTE: contract-only model (step 1) — no ToDomain()/domain type yet.
}

/// <summary>OrgnlGrpInf — identifies which original message is targeted by this cancellation.</summary>
public sealed record Camt055OriginalGroupInformationDto
{
    [JsonPropertyName("OriginalMessageId")]
    public string? OriginalMessageId { get; init; }

    /// <summary>Fixed to 'pain.013.001.11', 'pain.001.001.012' or 'camt.055.001.08'.</summary>
    [JsonPropertyName("OriginalMessageNameId")]
    public string? OriginalMessageNameId { get; init; }

    [JsonPropertyName("OriginalCreationDateTime")]
    public DateTime? OriginalCreationDateTime { get; init; }
}

/// <summary>CxlRsnInf — unlike camt.056, AdditionalInformation is mandatory (Max105Text) per Annex D.</summary>
public sealed record Camt055CancellationReasonDto
{
    [JsonPropertyName("OriginatorName")]
    public string? OriginatorName { get; init; }

    [JsonPropertyName("OriginatorBIC")]
    public string? OriginatorBic { get; init; }

    [JsonPropertyName("ReasonCode")]
    public string? ReasonCode { get; init; }

    [JsonPropertyName("AdditionalInformation")]
    public string? AdditionalInformation { get; init; }
}

/// <summary>
/// OrgnlTxRef — narrower than camt.056/pain.001: Annex D lists only DebtorAgent (institution level,
/// no full Debtor party/account) alongside a full Creditor party + account.
/// </summary>
public sealed record Camt055OriginalTransactionReferenceDto
{
    [JsonPropertyName("Currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("Amount")]
    public decimal? Amount { get; init; }

    [JsonPropertyName("RequestedExecutionDate")]
    public DateTime? RequestedExecutionDate { get; init; }

    [JsonPropertyName("ServiceLevelCode")]
    public string? ServiceLevelCode { get; init; }

    [JsonPropertyName("LocalInstrumentCode")]
    public string? LocalInstrumentCode { get; init; }

    [JsonPropertyName("CategoryPurposeCode")]
    public string? CategoryPurposeCode { get; init; }

    [JsonPropertyName("RemittanceInformationUnstructured")]
    public string? RemittanceInformationUnstructured { get; init; }

    [JsonPropertyName("DebtorAgent")]
    public Camt055AgentDto? DebtorAgent { get; init; }

    [JsonPropertyName("CreditorAgent")]
    public Camt055AgentDto? CreditorAgent { get; init; }

    [JsonPropertyName("Creditor")]
    public Camt055PartyDto? Creditor { get; init; }

    [JsonPropertyName("CreditorAccount")]
    public Camt055AccountDto? CreditorAccount { get; init; }
}

/// <summary>DbtrAgt / CdtrAgt — FinInstnId/BICFI (+ optional Name).</summary>
public sealed record Camt055AgentDto
{
    [JsonPropertyName("BICFI")]
    public string? Bicfi { get; init; }

    [JsonPropertyName("Name")]
    public string? Name { get; init; }
}

/// <summary>Cdtr — Name + optional Id + raw postal address.</summary>
public sealed record Camt055PartyDto
{
    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    [JsonPropertyName("Id")]
    public string? Id { get; init; }

    [JsonPropertyName("PostalAddress")]
    public JsonElement? PostalAddress { get; init; }
}

/// <summary>CdtrAcct — IBAN only, per Annex D.</summary>
public sealed record Camt055AccountDto
{
    [JsonPropertyName("IBAN")]
    public string? Iban { get; init; }
}
