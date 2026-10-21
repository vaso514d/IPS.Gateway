using System.Text.Json.Serialization;
using IPS.MiidleWear.Contracts.Pacs008;

namespace IPS.MiidleWear.Contracts.Pacs009;

/// <summary>
/// Gateway (StpApp) → core system: an incoming FI transfer (pacs.009.001.11, participant-to-participant liquidity lending),
/// delivered asynchronously. IPS has already moved the funds between the technical accounts and been acknowledged. The core
/// system durably takes the operation and answers HTTP 202 with <see cref="IncomingFiTransferStatuses.AcceptedForProcessing"/>;
/// the gateway then reads the result with the status query.
/// <para>
/// <see cref="TransactionId"/> is the gateway's identifier of this transfer: the core system uses it for correlation,
/// idempotency, duplicate protection, retries and the status query. The same transactionId is never a new operation.
/// </para>
/// </summary>
public sealed record IncomingFiTransferRequestDto
{
    /// <summary>REQUIRED. The gateway's identifier of the transfer (max 64): idempotency key and status reference.</summary>
    [JsonPropertyName("transactionId")]
    public string? TransactionId { get; init; }

    /// <summary>REQUIRED. <see cref="IncomingOperationTypes.FiTransfer"/>; the core system dispatches by it.</summary>
    [JsonPropertyName("operationType")]
    public string? OperationType { get; init; }

    /// <summary>REQUIRED. The business payload of the transfer.</summary>
    [JsonPropertyName("data")]
    public IncomingFiTransferDto? Data { get; init; }
}

/// <summary>The incoming FI transfer's business payload. Lengths follow the pacs.009.001.11 XSD.</summary>
public sealed record IncomingFiTransferDto
{
    /// <summary>REQUIRED. GrpHdr/MsgId (Max35Text). For audit and support; not the idempotency key.</summary>
    [JsonPropertyName("messageId")]
    public string? MessageId { get; init; }

    /// <summary>CdtTrfTxInf/PmtId/InstrId (Max35Text). Mandatory in the IPS profile (Annex D 8.1.3), optional in the XSD.</summary>
    [JsonPropertyName("instructionId")]
    public string? InstructionId { get; init; }

    /// <summary>REQUIRED. CdtTrfTxInf/PmtId/EndToEndId (Max35Text).</summary>
    [JsonPropertyName("endToEndId")]
    public string? EndToEndId { get; init; }

    /// <summary>CdtTrfTxInf/PmtId/TxId (Max35Text). Mandatory in the IPS profile, optional in the XSD.</summary>
    [JsonPropertyName("isoTransactionId")]
    public string? IsoTransactionId { get; init; }

    /// <summary>REQUIRED. When the gateway registered the transfer received from IPS.</summary>
    [JsonPropertyName("receivedAt")]
    public DateTimeOffset? ReceivedAt { get; init; }

    /// <summary>REQUIRED. CdtTrfTxInf/IntrBkSttlmAmt (18 digits, 5 fractional).</summary>
    [JsonPropertyName("amount")]
    public decimal? Amount { get; init; }

    /// <summary>REQUIRED. IntrBkSttlmAmt/@Ccy: 3 uppercase letters.</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    /// <summary>REQUIRED. IntrBkSttlmDt of the transaction, or of the group header when the transaction has none.</summary>
    [JsonPropertyName("settlementDate")]
    public DateOnly? SettlementDate { get; init; }

    /// <summary>REQUIRED. The lending participant.</summary>
    [JsonPropertyName("debtor")]
    public IncomingFiTransferDebtorDto? Debtor { get; init; }

    /// <summary>REQUIRED. The receiving participant: this bank or its indirect participant.</summary>
    [JsonPropertyName("creditor")]
    public IncomingFiTransferCreditorDto? Creditor { get; init; }

    /// <summary>OPTIONAL. GrpHdr/PmtTpInf/CtgyPurp (Cd, max 4).</summary>
    [JsonPropertyName("categoryPurposeCode")]
    public string? CategoryPurposeCode { get; init; }

    /// <summary>OPTIONAL. CdtTrfTxInf/Purp (Cd max 4, or Prtry max 35).</summary>
    [JsonPropertyName("purposeCode")]
    public string? PurposeCode { get; init; }

    /// <summary>OPTIONAL. CdtTrfTxInf/RmtInf/Ustrd; several Ustrd (each Max140Text) are joined into one text.</summary>
    [JsonPropertyName("remittanceInformation")]
    public string? RemittanceInformation { get; init; }
}

/// <summary>The lending participant.</summary>
public sealed record IncomingFiTransferDebtorDto
{
    /// <summary>REQUIRED. CdtTrfTxInf/DbtrAgt/FinInstnId/BICFI (or Dbtr when there is no DbtrAgt): direct or indirect participant.</summary>
    [JsonPropertyName("participantBic")]
    public string? ParticipantBic { get; init; }

    /// <summary>
    /// OPTIONAL. GrpHdr/InstgAgt/FinInstnId/BICFI, the direct participant that sent the message, when IPS keeps it in the
    /// forwarded message. Annex D 3.2 says IPS removes InstgAgt from forwarded messages, so it is usually absent.
    /// </summary>
    [JsonPropertyName("directParticipantBic")]
    public string? DirectParticipantBic { get; init; }

    /// <summary>OPTIONAL. CdtTrfTxInf/DbtrAcct/Id/IBAN (max 34).</summary>
    [JsonPropertyName("accountIban")]
    public string? AccountIban { get; init; }
}

/// <summary>The receiving participant.</summary>
public sealed record IncomingFiTransferCreditorDto
{
    /// <summary>REQUIRED. CdtTrfTxInf/CdtrAgt/FinInstnId/BICFI (or Cdtr when there is no CdtrAgt).</summary>
    [JsonPropertyName("participantBic")]
    public string? ParticipantBic { get; init; }

    /// <summary>OPTIONAL. CdtTrfTxInf/CdtrAcct/Id/IBAN (max 34).</summary>
    [JsonPropertyName("accountIban")]
    public string? AccountIban { get; init; }
}

/// <summary>
/// Core system → gateway: the answer to an incoming FI transfer and to its status query. COMPLETED and REJECTED are
/// final; ACCEPTED_FOR_PROCESSING and PROCESSING are not, and the gateway keeps asking. REJECTED is final for this
/// transactionId: whatever the core system does with the funds afterwards is its own new operation, under its own
/// core reference.
/// </summary>
public sealed record IncomingFiTransferResultDto
{
    /// <summary>REQUIRED. The delivered transactionId.</summary>
    [JsonPropertyName("transactionId")]
    public string? TransactionId { get; init; }

    /// <summary>REQUIRED. One of <see cref="IncomingFiTransferStatuses"/>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>REQUIRED with REJECTED. An ISO 20022 external status reason code (max 4).</summary>
    [JsonPropertyName("reasonCode")]
    public string? ReasonCode { get; init; }

    /// <summary>OPTIONAL. Why it was rejected (max 105).</summary>
    [JsonPropertyName("reasonDescription")]
    public string? ReasonDescription { get; init; }

    /// <summary>OPTIONAL with COMPLETED. The core system's posting reference.</summary>
    [JsonPropertyName("coreReference")]
    public string? CoreReference { get; init; }

    /// <summary>ACCEPTED_FOR_PROCESSING: durably taken, not yet processed.</summary>
    public static IncomingFiTransferResultDto AcceptedForProcessing(string transactionId) =>
        new() { TransactionId = transactionId, Status = IncomingFiTransferStatuses.AcceptedForProcessing };

    /// <summary>PROCESSING: being processed.</summary>
    public static IncomingFiTransferResultDto Processing(string transactionId) =>
        new() { TransactionId = transactionId, Status = IncomingFiTransferStatuses.Processing };

    /// <summary>COMPLETED, with the posting.</summary>
    public static IncomingFiTransferResultDto Completed(string transactionId, string? coreReference = null) =>
        new() { TransactionId = transactionId, Status = IncomingFiTransferStatuses.Completed, CoreReference = coreReference };

    /// <summary>REJECTED with an ISO reason code.</summary>
    public static IncomingFiTransferResultDto Rejected(string transactionId, string reasonCode, string? reasonDescription = null) =>
        new() { TransactionId = transactionId, Status = IncomingFiTransferStatuses.Rejected, ReasonCode = reasonCode, ReasonDescription = reasonDescription };
}

/// <summary>The values of <see cref="IncomingFiTransferResultDto.Status"/>.</summary>
public static class IncomingFiTransferStatuses
{
    /// <summary>The core system durably took the operation; not final.</summary>
    public const string AcceptedForProcessing = "ACCEPTED_FOR_PROCESSING";

    /// <summary>Being processed; not final.</summary>
    public const string Processing = "PROCESSING";

    /// <summary>Processed; final.</summary>
    public const string Completed = "COMPLETED";

    /// <summary>Refused; final.</summary>
    public const string Rejected = "REJECTED";
}

/// <summary>The routes the core system serves for incoming FI transfers.</summary>
public static class IncomingFiTransferRestApiRoutes
{
    /// <summary>POST: deliver an incoming FI transfer.</summary>
    public const string Receive = "/stp/fi-transfers";

    /// <summary>GET: the result for a transactionId; 404 when the transfer never arrived.</summary>
    public const string Status = "/stp/fi-transfers/{transactionId}";
}
