using System.Text.Json.Serialization;

namespace IPS.MiidleWear.Contracts.Pacs008;

/// <summary>
/// Gateway (StpApp) → core system: an incoming instant payment (pacs.008.001.12), delivered synchronously. The core system
/// completes its processing within the request and answers ACCEPTED or REJECTED with <see cref="IncomingPaymentResultDto"/>.
/// <para>
/// <see cref="TransactionId"/> is the gateway's identifier of this payment: the core system uses it for correlation,
/// idempotency, duplicate protection and the status query. The same transactionId is never a new payment.
/// </para>
/// </summary>
public sealed record IncomingPaymentRequestDto
{
    /// <summary>REQUIRED. The gateway's identifier of the payment (max 64): idempotency key and status reference.</summary>
    [JsonPropertyName("transactionId")]
    public string? TransactionId { get; init; }

    /// <summary>REQUIRED. <see cref="IncomingOperationTypes.InstantPayment"/>; the core system dispatches by it.</summary>
    [JsonPropertyName("operationType")]
    public string? OperationType { get; init; }

    /// <summary>REQUIRED. The business payload of the payment.</summary>
    [JsonPropertyName("data")]
    public IncomingInstantPaymentDto? Data { get; init; }
}

/// <summary>The incoming payment's business payload.</summary>
public sealed record IncomingInstantPaymentDto
{
    /// <summary>REQUIRED. GrpHdr/MsgId (max 35). For audit and support; not the idempotency key.</summary>
    [JsonPropertyName("messageId")]
    public string? MessageId { get; init; }

    /// <summary>REQUIRED. CdtTrfTxInf/PmtId/InstrId (max 35).</summary>
    [JsonPropertyName("instructionId")]
    public string? InstructionId { get; init; }

    /// <summary>REQUIRED. CdtTrfTxInf/PmtId/EndToEndId (max 35): the business reference kept end to end.</summary>
    [JsonPropertyName("endToEndId")]
    public string? EndToEndId { get; init; }

    /// <summary>REQUIRED. CdtTrfTxInf/PmtId/TxId (max 35).</summary>
    [JsonPropertyName("isoTransactionId")]
    public string? IsoTransactionId { get; init; }

    /// <summary>REQUIRED. When the gateway received the payment from IPS.</summary>
    [JsonPropertyName("receivedAt")]
    public DateTimeOffset? ReceivedAt { get; init; }

    /// <summary>REQUIRED. GrpHdr/CreDtTm.</summary>
    [JsonPropertyName("creationDateTime")]
    public DateTimeOffset? CreationDateTime { get; init; }

    /// <summary>REQUIRED. CdtTrfTxInf/AccptncDtTm.</summary>
    [JsonPropertyName("acceptanceDateTime")]
    public DateTimeOffset? AcceptanceDateTime { get; init; }

    /// <summary>REQUIRED. CdtTrfTxInf/IntrBkSttlmAmt (13 integer and 5 fractional digits).</summary>
    [JsonPropertyName("amount")]
    public decimal? Amount { get; init; }

    /// <summary>REQUIRED. ISO 4217 code, 3 letters.</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    /// <summary>OPTIONAL. PmtTpInf/InstrPrty: NORM or HIGH.</summary>
    [JsonPropertyName("instructionPriority")]
    public string? InstructionPriority { get; init; }

    /// <summary>OPTIONAL. PmtTpInf/CtgyPurp/Cd (max 4).</summary>
    [JsonPropertyName("categoryPurposeCode")]
    public string? CategoryPurposeCode { get; init; }

    /// <summary>REQUIRED. The payer.</summary>
    [JsonPropertyName("debtor")]
    public IncomingDebtorDto? Debtor { get; init; }

    /// <summary>REQUIRED. The payee, a customer of this bank.</summary>
    [JsonPropertyName("creditor")]
    public IncomingCreditorDto? Creditor { get; init; }

    /// <summary>OPTIONAL. Present when the account debtor and the actual payer differ.</summary>
    [JsonPropertyName("ultimateDebtor")]
    public IncomingUltimatePartyDto? UltimateDebtor { get; init; }

    /// <summary>OPTIONAL. Present when the creditor account holder and the final beneficiary differ.</summary>
    [JsonPropertyName("ultimateCreditor")]
    public IncomingUltimatePartyDto? UltimateCreditor { get; init; }

    /// <summary>OPTIONAL. RgltryRptg/Dtls: initiation channel and payer geolocation.</summary>
    [JsonPropertyName("paymentInitiation")]
    public Pacs008PaymentInitiationDto? PaymentInitiation { get; init; }

    /// <summary>OPTIONAL. RltdRmtInf: initiation channel and instrument.</summary>
    [JsonPropertyName("initiationChannelInstrument")]
    public Pacs008InitiationChannelInstrumentDto? InitiationChannelInstrument { get; init; }

    /// <summary>OPTIONAL. RmtInf.</summary>
    [JsonPropertyName("remittance")]
    public Pacs008RemittanceDto? Remittance { get; init; }
}

/// <summary>The payer.</summary>
public sealed record IncomingDebtorDto
{
    /// <summary>REQUIRED. Dbtr/Nm (max 140).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>OPTIONAL. 0 = organisation (Dbtr/Id/OrgId), 1 = individual (Dbtr/Id/PrvtId); present with an identifier.</summary>
    [JsonPropertyName("partyType")]
    public int? PartyType { get; init; }

    /// <summary>OPTIONAL. Dbtr/Id/…/Othr/Id (max 35): tax code or personal number, by <see cref="PartyType"/>.</summary>
    [JsonPropertyName("identifier")]
    public string? Identifier { get; init; }

    /// <summary>OPTIONAL. The second Dbtr/Id/…/Othr with SchmeNm/Cd BILL (max 35).</summary>
    [JsonPropertyName("billIdentifier")]
    public string? BillIdentifier { get; init; }

    /// <summary>OPTIONAL. Dbtr/PstlAdr.</summary>
    [JsonPropertyName("address")]
    public Pacs008PostalAddressDto? Address { get; init; }

    /// <summary>REQUIRED. DbtrAgt/FinInstnId/BICFI (8 or 11): the direct participant the payment came from.</summary>
    [JsonPropertyName("participantBic")]
    public string? ParticipantBic { get; init; }

    /// <summary>OPTIONAL. DbtrAgt/FinInstnId/ClrSysMmbId/MmbId (max 35): when the debtor belongs to an indirect participant.</summary>
    [JsonPropertyName("indirectParticipantBic")]
    public string? IndirectParticipantBic { get; init; }

    /// <summary>REQUIRED. DbtrAcct.</summary>
    [JsonPropertyName("account")]
    public IncomingAccountDto? Account { get; init; }
}

/// <summary>The payee, a customer of this bank.</summary>
public sealed record IncomingCreditorDto
{
    /// <summary>OPTIONAL. Cdtr/Nm (max 140).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>OPTIONAL. 0 = organisation (Cdtr/Id/OrgId), 1 = individual (Cdtr/Id/PrvtId); present with an identifier.</summary>
    [JsonPropertyName("partyType")]
    public int? PartyType { get; init; }

    /// <summary>OPTIONAL. Cdtr/Id/…/Othr/Id (max 35).</summary>
    [JsonPropertyName("identifier")]
    public string? Identifier { get; init; }

    /// <summary>OPTIONAL. Cdtr/PstlAdr.</summary>
    [JsonPropertyName("address")]
    public Pacs008PostalAddressDto? Address { get; init; }

    /// <summary>OPTIONAL. CdtrAgt/FinInstnId/ClrSysMmbId/MmbId (max 35): when the creditor belongs to this bank's indirect participant.</summary>
    [JsonPropertyName("indirectParticipantBic")]
    public string? IndirectParticipantBic { get; init; }

    /// <summary>REQUIRED. CdtrAcct.</summary>
    [JsonPropertyName("account")]
    public IncomingAccountDto? Account { get; init; }
}

/// <summary>An account: an IBAN (…Acct/Id/IBAN) or a Treasury code (…Acct/Id/Othr/Id).</summary>
public sealed record IncomingAccountDto
{
    /// <summary>REQUIRED. <see cref="IncomingAccountTypes.Iban"/> (0) or <see cref="IncomingAccountTypes.TreasuryCode"/> (1).</summary>
    [JsonPropertyName("type")]
    public int? Type { get; init; }

    /// <summary>The IBAN (max 34) when <see cref="Type"/> is 0; otherwise null.</summary>
    [JsonPropertyName("iban")]
    public string? Iban { get; init; }

    /// <summary>The Treasury code (max 34) when <see cref="Type"/> is 1; otherwise null.</summary>
    [JsonPropertyName("treasuryCode")]
    public string? TreasuryCode { get; init; }
}

/// <summary>UltmtDbtr / UltmtCdtr.</summary>
public sealed record IncomingUltimatePartyDto
{
    /// <summary>OPTIONAL. …/Nm (max 140).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>OPTIONAL. 0 = organisation, 1 = individual; present with an identifier.</summary>
    [JsonPropertyName("partyType")]
    public int? PartyType { get; init; }

    /// <summary>OPTIONAL. …/Id/…/Othr/Id (max 35).</summary>
    [JsonPropertyName("identifier")]
    public string? Identifier { get; init; }
}

/// <summary>
/// Core system → gateway: the answer to an incoming payment, and to its status query. ACCEPTED and REJECTED are final;
/// PENDING (status query only) means still processing. The answer becomes the pacs.002 to IPS.
/// </summary>
public sealed record IncomingPaymentResultDto
{
    /// <summary>REQUIRED. The delivered transactionId.</summary>
    [JsonPropertyName("transactionId")]
    public string? TransactionId { get; init; }

    /// <summary>REQUIRED. ACCEPTED, REJECTED, or PENDING (status query only).</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>REQUIRED with REJECTED. An ISO 20022 external status reason code (max 4, e.g. AC01, AC04, AM04).</summary>
    [JsonPropertyName("reasonCode")]
    public string? ReasonCode { get; init; }

    /// <summary>OPTIONAL. Why it was rejected (max 105, the pacs.002 AddtlInf).</summary>
    [JsonPropertyName("reasonDescription")]
    public string? ReasonDescription { get; init; }

    /// <summary>OPTIONAL with ACCEPTED. The core system's posting reference: the gateway uses it for reconciliation and reversal.</summary>
    [JsonPropertyName("coreReference")]
    public string? CoreReference { get; init; }

    /// <summary>ACCEPTED.</summary>
    public static IncomingPaymentResultDto Accepted(string transactionId, string? coreReference = null) =>
        new() { TransactionId = transactionId, Status = IncomingPaymentStatuses.Accepted, CoreReference = coreReference };

    /// <summary>REJECTED with an ISO reason code.</summary>
    public static IncomingPaymentResultDto Rejected(string transactionId, string reasonCode, string? reasonDescription = null) =>
        new() { TransactionId = transactionId, Status = IncomingPaymentStatuses.Rejected, ReasonCode = reasonCode, ReasonDescription = reasonDescription };

    /// <summary>PENDING: status query only, while the payment is still processing.</summary>
    public static IncomingPaymentResultDto Pending(string transactionId) =>
        new() { TransactionId = transactionId, Status = IncomingPaymentStatuses.Pending };
}

/// <summary>The values of <see cref="IncomingPaymentResultDto.Status"/>.</summary>
public static class IncomingPaymentStatuses
{
    /// <summary>The core system completed its processing and accepted the payment.</summary>
    public const string Accepted = "ACCEPTED";

    /// <summary>The core system completed its processing and refused the payment.</summary>
    public const string Rejected = "REJECTED";

    /// <summary>Status query only: still processing.</summary>
    public const string Pending = "PENDING";
}

/// <summary>The values of <see cref="IncomingPaymentRequestDto.OperationType"/>.</summary>
public static class IncomingOperationTypes
{
    /// <summary>An incoming instant payment, pacs.008.</summary>
    public const string InstantPayment = "INSTANT_PAYMENT";
}

/// <summary>The values of <see cref="IncomingAccountDto.Type"/>.</summary>
public static class IncomingAccountTypes
{
    /// <summary>…Acct/Id/IBAN.</summary>
    public const int Iban = 0;

    /// <summary>…Acct/Id/Othr/Id: a Treasury code.</summary>
    public const int TreasuryCode = 1;
}

/// <summary>The routes the core system serves for incoming instant payments.</summary>
public static class IncomingPaymentRestApiRoutes
{
    /// <summary>POST: deliver an incoming payment.</summary>
    public const string Receive = "/stp/incoming-payments";

    /// <summary>GET: the result for a transactionId; 404 when the payment never arrived.</summary>
    public const string Status = "/stp/incoming-payments/{transactionId}";
}
