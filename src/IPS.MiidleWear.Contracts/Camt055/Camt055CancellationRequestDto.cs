using System.Text.Json;
using System.Text.Json.Serialization;

namespace IPS.MiidleWear.Contracts.Camt055;

/// <summary>
/// JSON REST contract for camt.055.001.012 (Customer Payment Cancellation Request), modeled after
/// Annex D §7.1.13. Used either as a Request for Cancellation (RfC) of a previously sent pain.001 or
/// pain.013 message, or as a Request for Status Update (RFSU) on an already-submitted cancellation.
/// A single TxInf occurrence is enforced by IPS, so this DTO is flattened the same way the other
/// contract-only models in this solution are.
/// <para>
/// Delivered to the core system at <see cref="Camt055RestApiRoutes.Receive"/> for an incoming camt.055.001.12 about a
/// pain.001 this bank received; the debtor agent is this participant. Every field is filled from the received message;
/// date-times are given in UTC, <see cref="Camt055OriginalTransactionReferenceDto.RequestedExecutionDate"/> as a date at
/// midnight, and <see cref="Camt055PartyDto.PostalAddress"/> in the shape of the camt.056 postal address
/// (StreetName, BuildingNumber, PostCode, TownName, CountrySubDivision, Country, AddressLines). Idempotency-Key = <see cref="MsgId"/>.
/// </para>
/// </summary>
public sealed record Camt055CancellationRequestDto
{
    // ---- Group Header (Assgnmt) ----

    /// <summary>Assgnmt/Id: the request's message id; also the Idempotency-Key and the payment-status reference.</summary>
    [JsonPropertyName("MsgId")]
    public string? MsgId { get; init; }

    /// <summary>Assgnmt/Assgnr/Agt/FinInstnId/BICFI: the participant that sent the request.</summary>
    [JsonPropertyName("AssignerBIC")]
    public string? AssignerBic { get; init; }

    /// <summary>Assgnmt/Assgne/Agt/FinInstnId/BICFI: the participant the request is addressed to.</summary>
    [JsonPropertyName("AssigneeBIC")]
    public string? AssigneeBic { get; init; }

    /// <summary>Assgnmt/CreDtTm: when the request was created, in UTC.</summary>
    [JsonPropertyName("CreDtTm")]
    public DateTime? CreDtTm { get; init; }

    // ---- Original Payment Information and Cancellation (OrgnlPmtInfAndCxl) ----

    /// <summary>OrgnlPmtInfAndCxl/PmtCxlId: the sender's id of this cancellation, when given.</summary>
    [JsonPropertyName("PaymentCancellationId")]
    public string? PaymentCancellationId { get; init; }

    /// <summary>OrgnlPmtInfAndCxl/OrgnlPmtInfId: the PmtInfId of the pain.001 to cancel (the paymentInformationId this bank received).</summary>
    [JsonPropertyName("OriginalPaymentInformationId")]
    public string? OriginalPaymentInformationId { get; init; }

    /// <summary>OrgnlPmtInfAndCxl/OrgnlGrpInf: the original message, when given.</summary>
    [JsonPropertyName("OriginalGroupInformation")]
    public Camt055OriginalGroupInformationDto? OriginalGroupInformation { get; init; }

    // ---- Transaction Information (TxInf) ----

    /// <summary>TxInf/CxlId: the sender's id of the cancelled transaction, when given.</summary>
    [JsonPropertyName("CancellationId")]
    public string? CancellationId { get; init; }

    /// <summary>TxInf/OrgnlInstrId: the instruction id of the original transaction, when given.</summary>
    [JsonPropertyName("OriginalInstructionId")]
    public string? OriginalInstructionId { get; init; }

    /// <summary>TxInf/OrgnlEndToEndId: the end-to-end id of the original transaction, when given.</summary>
    [JsonPropertyName("OriginalEndToEndId")]
    public string? OriginalEndToEndId { get; init; }

    /// <summary>TxInf/CxlRsnInf, or OrgnlPmtInfAndCxl/CxlRsnInf when the transaction has none.</summary>
    [JsonPropertyName("CancellationReason")]
    public Camt055CancellationReasonDto? CancellationReason { get; init; }

    /// <summary>TxInf/OrgnlTxRef: the details of the initiation to cancel.</summary>
    [JsonPropertyName("OriginalTransaction")]
    public Camt055OriginalTransactionReferenceDto? OriginalTransaction { get; init; }

    // NOTE: contract-only model (step 1) — no ToDomain()/domain type yet.
}

/// <summary>OrgnlGrpInf — identifies which original message is targeted by this cancellation.</summary>
public sealed record Camt055OriginalGroupInformationDto
{
    /// <summary>OrgnlGrpInf/OrgnlMsgId: the message id of the original message.</summary>
    [JsonPropertyName("OriginalMessageId")]
    public string? OriginalMessageId { get; init; }

    /// <summary>Fixed to 'pain.013.001.11', 'pain.001.001.012' or 'camt.055.001.08'.</summary>
    [JsonPropertyName("OriginalMessageNameId")]
    public string? OriginalMessageNameId { get; init; }

    /// <summary>OrgnlGrpInf/OrgnlCreDtTm: when the original message was created, in UTC, when given.</summary>
    [JsonPropertyName("OriginalCreationDateTime")]
    public DateTime? OriginalCreationDateTime { get; init; }
}

/// <summary>CxlRsnInf — unlike camt.056, AdditionalInformation is mandatory (Max105Text) per Annex D.</summary>
public sealed record Camt055CancellationReasonDto
{
    /// <summary>CxlRsnInf/Orgtr/Nm: who asked for the cancellation, when given.</summary>
    [JsonPropertyName("OriginatorName")]
    public string? OriginatorName { get; init; }

    /// <summary>CxlRsnInf/Orgtr/Id/OrgId/AnyBIC: the BIC of who asked for the cancellation, when given.</summary>
    [JsonPropertyName("OriginatorBIC")]
    public string? OriginatorBic { get; init; }

    /// <summary>CxlRsnInf/Rsn/Cd (or Prtry): the cancellation reason code.</summary>
    [JsonPropertyName("ReasonCode")]
    public string? ReasonCode { get; init; }

    /// <summary>CxlRsnInf/AddtlInf: free text explaining the reason.</summary>
    [JsonPropertyName("AdditionalInformation")]
    public string? AdditionalInformation { get; init; }
}

/// <summary>
/// OrgnlTxRef — narrower than camt.056/pain.001: Annex D lists only DebtorAgent (institution level,
/// no full Debtor party/account) alongside a full Creditor party + account.
/// </summary>
public sealed record Camt055OriginalTransactionReferenceDto
{
    /// <summary>OrgnlTxRef/Amt/InstdAmt/@Ccy: the currency of the original instructed amount.</summary>
    [JsonPropertyName("Currency")]
    public string? Currency { get; init; }

    /// <summary>OrgnlTxRef/Amt/InstdAmt: the original instructed amount.</summary>
    [JsonPropertyName("Amount")]
    public decimal? Amount { get; init; }

    /// <summary>OrgnlTxRef/ReqdExctnDt: the requested execution date, as the date at midnight.</summary>
    [JsonPropertyName("RequestedExecutionDate")]
    public DateTime? RequestedExecutionDate { get; init; }

    /// <summary>OrgnlTxRef/PmtTpInf/SvcLvl code, when given.</summary>
    [JsonPropertyName("ServiceLevelCode")]
    public string? ServiceLevelCode { get; init; }

    /// <summary>OrgnlTxRef/PmtTpInf/LclInstrm code, when given.</summary>
    [JsonPropertyName("LocalInstrumentCode")]
    public string? LocalInstrumentCode { get; init; }

    /// <summary>OrgnlTxRef/PmtTpInf/CtgyPurp code, when given.</summary>
    [JsonPropertyName("CategoryPurposeCode")]
    public string? CategoryPurposeCode { get; init; }

    /// <summary>OrgnlTxRef/RmtInf/Ustrd lines, joined, when given.</summary>
    [JsonPropertyName("RemittanceInformationUnstructured")]
    public string? RemittanceInformationUnstructured { get; init; }

    /// <summary>OrgnlTxRef/DbtrAgt: the debtor agent, this participant.</summary>
    [JsonPropertyName("DebtorAgent")]
    public Camt055AgentDto? DebtorAgent { get; init; }

    /// <summary>OrgnlTxRef/CdtrAgt: the creditor agent, when given.</summary>
    [JsonPropertyName("CreditorAgent")]
    public Camt055AgentDto? CreditorAgent { get; init; }

    /// <summary>OrgnlTxRef/Cdtr/Pty: the creditor, when given.</summary>
    [JsonPropertyName("Creditor")]
    public Camt055PartyDto? Creditor { get; init; }

    /// <summary>OrgnlTxRef/CdtrAcct: the creditor account, when given.</summary>
    [JsonPropertyName("CreditorAccount")]
    public Camt055AccountDto? CreditorAccount { get; init; }
}

/// <summary>DbtrAgt / CdtrAgt — FinInstnId/BICFI (+ optional Name).</summary>
public sealed record Camt055AgentDto
{
    /// <summary>FinInstnId/BICFI: the agent's BIC.</summary>
    [JsonPropertyName("BICFI")]
    public string? Bicfi { get; init; }

    /// <summary>FinInstnId/Nm: the agent's name, when given.</summary>
    [JsonPropertyName("Name")]
    public string? Name { get; init; }
}

/// <summary>Cdtr — Name + optional Id + raw postal address.</summary>
public sealed record Camt055PartyDto
{
    /// <summary>Nm: the creditor's name.</summary>
    [JsonPropertyName("Name")]
    public string? Name { get; init; }

    /// <summary>The creditor's identification (organisation or private), when given.</summary>
    [JsonPropertyName("Id")]
    public string? Id { get; init; }

    /// <summary>PstlAdr in the camt.056 address shape (StreetName, BuildingNumber, PostCode, TownName, CountrySubDivision, Country, AddressLines), when given.</summary>
    [JsonPropertyName("PostalAddress")]
    public JsonElement? PostalAddress { get; init; }
}

/// <summary>CdtrAcct — IBAN only, per Annex D.</summary>
public sealed record Camt055AccountDto
{
    /// <summary>Id/IBAN: the account's IBAN.</summary>
    [JsonPropertyName("IBAN")]
    public string? Iban { get; init; }
}

public static class Camt055RestApiRoutes
{
    /// <summary>Implemented by the core system; called by the gateway for an incoming camt.055 cancellation request.</summary>
    public const string Receive = "/api/ips/camt055/receive";
}
