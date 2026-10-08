using System.Text.Json.Serialization;

namespace IPS.MiidleWear.Contracts.Pacs008;

/// <summary>
/// Bank/Core → gateway instant-payment request (outbound pacs.008.001.12), per the Bank/Core contract
/// "pacs.008.001.12-instant-payment-request-outbound_v1". Carries business intent only: the gateway
/// generates every technical XML value itself — AppHdr, GrpHdr/MsgId, PmtId/TxId, IntrBkSttlmDt, the
/// sender BIC (DbtrAgt/InstgAgt), fixed codes (CLRG, IPS, INST, SLEV, ClrSysId "GE", SchmeNm "BILL") and the signature.
/// <para>
/// <see cref="ClientReference"/> is the idempotency key: a retry with the same value returns the existing
/// transaction instead of sending a second payment. It is never written to the XML.
/// </para>
/// </summary>
public sealed record Pacs008InstantPaymentRequestDto
{
    /// <summary>REQUIRED. Idempotency key assigned by Bank/Core; reused unchanged on retries. Not mapped to XML.</summary>
    [JsonPropertyName("clientReference")]
    public string? ClientReference { get; init; }

    /// <summary>REQUIRED. CdtTrfTxInf/PmtId/InstrId (Max35Text). Stable across retries.</summary>
    [JsonPropertyName("instructionId")]
    public string? InstructionId { get; init; }

    /// <summary>REQUIRED. CdtTrfTxInf/PmtId/EndToEndId (Max35Text). "RTP-…" for RTP, "PSP-…" for pain.001 initiated payments.</summary>
    [JsonPropertyName("endToEndId")]
    public string? EndToEndId { get; init; }

    /// <summary>REQUIRED. GrpHdr/CreDtTm: when the customer confirmed the payment. Stable across retries.</summary>
    [JsonPropertyName("creationDateTime")]
    public DateTimeOffset? CreationDateTime { get; init; }

    /// <summary>
    /// REQUIRED. CdtTrfTxInf/AccptncDtTm: when the debtor participant received the instruction — the IPS SLA
    /// reference point. 0 ≤ acceptanceDateTime − creationDateTime ≤ 1 second. Its local date is GrpHdr/IntrBkSttlmDt.
    /// </summary>
    [JsonPropertyName("acceptanceDateTime")]
    public DateTimeOffset? AcceptanceDateTime { get; init; }

    /// <summary>REQUIRED. GrpHdr/TtlIntrBkSttlmAmt and CdtTrfTxInf/IntrBkSttlmAmt (one transaction per message).</summary>
    [JsonPropertyName("amount")]
    public decimal? Amount { get; init; }

    /// <summary>REQUIRED. @Ccy of both settlement amounts.</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    /// <summary>REQUIRED. GrpHdr/PmtTpInf/InstrPrty: "NORM" or "HIGH" (HIGH = Fast RTP).</summary>
    [JsonPropertyName("instructionPriority")]
    public string? InstructionPriority { get; init; }

    /// <summary>OPTIONAL. GrpHdr/PmtTpInf/CtgyPurp/Cd. Omitted → no CtgyPurp element (no default).</summary>
    [JsonPropertyName("categoryPurposeCode")]
    public string? CategoryPurposeCode { get; init; }

    /// <summary>REQUIRED. Payer: Dbtr, DbtrAcct and the indirect-participant part of DbtrAgt.</summary>
    [JsonPropertyName("debtor")]
    public Pacs008DebtorRequestDto? Debtor { get; init; }

    /// <summary>REQUIRED. Payee: Cdtr, CdtrAcct and CdtrAgt.</summary>
    [JsonPropertyName("creditor")]
    public Pacs008CreditorRequestDto? Creditor { get; init; }

    /// <summary>OPTIONAL. CdtTrfTxInf/UltmtDbtr — only when the account debtor is not the actual payer.</summary>
    [JsonPropertyName("ultimateDebtor")]
    public Pacs008UltimatePartyRequestDto? UltimateDebtor { get; init; }

    /// <summary>OPTIONAL. CdtTrfTxInf/UltmtCdtr — only when the account creditor is not the final beneficiary.</summary>
    [JsonPropertyName("ultimateCreditor")]
    public Pacs008UltimatePartyRequestDto? UltimateCreditor { get; init; }

    /// <summary>OPTIONAL. CdtTrfTxInf/RgltryRptg/Dtls: initiation channel and payer geolocation.</summary>
    [JsonPropertyName("paymentInitiation")]
    public Pacs008PaymentInitiationDto? PaymentInitiation { get; init; }

    /// <summary>OPTIONAL. Annex D initiation channel/instrument, sent as CdtTrfTxInf/RltdRmtInf.</summary>
    [JsonPropertyName("initiationChannelInstrument")]
    public Pacs008InitiationChannelInstrumentDto? InitiationChannelInstrument { get; init; }

    /// <summary>OPTIONAL. CdtTrfTxInf/RmtInf.</summary>
    [JsonPropertyName("remittance")]
    public Pacs008RemittanceDto? Remittance { get; init; }
}

/// <summary>Payer. On a send the debtor bank is always the gateway's own participant BIC (IpsStp:ParticipantBic).</summary>
public sealed record Pacs008DebtorRequestDto
{
    /// <summary>
    /// INBOUND ONLY (IPS → core). DbtrAgt/FinInstnId/BICFI — the payer's direct IPS participant. On a send it is
    /// always the gateway's own BIC, so it may be omitted; a different value is rejected.
    /// </summary>
    [JsonPropertyName("participantBic")]
    public string? ParticipantBic { get; init; }

    /// <summary>REQUIRED. 0 = organisation (Dbtr/Id/OrgId), 1 = individual (Dbtr/Id/PrvtId).</summary>
    [JsonPropertyName("type")]
    public int? Type { get; init; }

    /// <summary>REQUIRED. Dbtr/Nm.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>CONDITIONALLY REQUIRED. Dbtr/Id/OrgId|PrvtId/Othr/Id (branch chosen by <see cref="Type"/>).</summary>
    [JsonPropertyName("identifier")]
    public string? Identifier { get; init; }

    /// <summary>
    /// OPTIONAL (BILL payments). A second Dbtr/Id/…/Othr with SchmeNm/Cd = "BILL". Not the same concept as a
    /// structured BILL remittance reference.
    /// </summary>
    [JsonPropertyName("billIdentifier")]
    public string? BillIdentifier { get; init; }

    /// <summary>OPTIONAL. Dbtr/PstlAdr (Rulebook AT-03).</summary>
    [JsonPropertyName("address")]
    public Pacs008PostalAddressDto? Address { get; init; }

    /// <summary>OPTIONAL. DbtrAgt/FinInstnId/ClrSysMmbId/MmbId (ClrSysId/Cd = "GE"): the debtor's indirect participant (Max35Text).</summary>
    [JsonPropertyName("indirectParticipantBic")]
    public string? IndirectParticipantBic { get; init; }

    /// <summary>REQUIRED. DbtrAcct/Id/IBAN.</summary>
    [JsonPropertyName("account")]
    public string? Account { get; init; }
}

/// <summary>Payee, including the creditor's bank — the only place the receiving participant appears in pacs.008.</summary>
public sealed record Pacs008CreditorRequestDto
{
    /// <summary>REQUIRED. 0 = organisation (Cdtr/Id/OrgId), 1 = individual (Cdtr/Id/PrvtId).</summary>
    [JsonPropertyName("type")]
    public int? Type { get; init; }

    /// <summary>REQUIRED. Cdtr/Nm.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>CONDITIONALLY REQUIRED. Cdtr/Id/OrgId|PrvtId/Othr/Id (branch chosen by <see cref="Type"/>).</summary>
    [JsonPropertyName("identifier")]
    public string? Identifier { get; init; }

    /// <summary>OPTIONAL. Cdtr/PstlAdr (Rulebook AT-22).</summary>
    [JsonPropertyName("address")]
    public Pacs008PostalAddressDto? Address { get; init; }

    /// <summary>
    /// REQUIRED. CdtrAgt/FinInstnId/BICFI — the creditor's direct IPS participant (receiving bank, the direct
    /// participant of an indirect one, or Treasury). IPS routes the payment by this value (Annex D §3.2).
    /// </summary>
    [JsonPropertyName("participantBic")]
    public string? ParticipantBic { get; init; }

    /// <summary>OPTIONAL. CdtrAgt/FinInstnId/ClrSysMmbId/MmbId (ClrSysId/Cd = "GE"): the creditor's indirect participant (Max35Text).</summary>
    [JsonPropertyName("indirectParticipantBic")]
    public string? IndirectParticipantBic { get; init; }

    /// <summary>REQUIRED. CdtrAcct/Id/IBAN (Treasury receivers are addressed by IBAN too).</summary>
    [JsonPropertyName("account")]
    public string? Account { get; init; }
}

/// <summary>UltmtDbtr / UltmtCdtr: name and identification only.</summary>
public sealed record Pacs008UltimatePartyRequestDto
{
    /// <summary>REQUIRED when the object is present. 0 = organisation, 1 = individual.</summary>
    [JsonPropertyName("type")]
    public int? Type { get; init; }

    /// <summary>REQUIRED when the object is present. …/Nm.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>CONDITIONALLY REQUIRED. …/Id/OrgId|PrvtId/Othr/Id.</summary>
    [JsonPropertyName("identifier")]
    public string? Identifier { get; init; }
}

/// <summary>Practical v1 subset of PostalAddress27. Every field is optional.</summary>
public sealed record Pacs008PostalAddressDto
{
    /// <summary>PstlAdr/StrtNm (Max140Text).</summary>
    [JsonPropertyName("streetName")]
    public string? StreetName { get; init; }

    /// <summary>PstlAdr/BldgNb (Max16Text).</summary>
    [JsonPropertyName("buildingNumber")]
    public string? BuildingNumber { get; init; }

    /// <summary>PstlAdr/PstCd (Max16Text).</summary>
    [JsonPropertyName("postCode")]
    public string? PostCode { get; init; }

    /// <summary>PstlAdr/TwnNm (Max140Text).</summary>
    [JsonPropertyName("townName")]
    public string? TownName { get; init; }

    /// <summary>PstlAdr/CtrySubDvsn (Max35Text).</summary>
    [JsonPropertyName("countrySubdivision")]
    public string? CountrySubdivision { get; init; }

    /// <summary>PstlAdr/Ctry: ISO 3166-1 alpha-2, two uppercase letters.</summary>
    [JsonPropertyName("country")]
    public string? Country { get; init; }

    /// <summary>PstlAdr/AdrLine: up to 490 characters, split into consecutive ≤70-character AdrLine elements (max 7).</summary>
    [JsonPropertyName("addressLines")]
    public string? AddressLines { get; init; }
}

/// <summary>CdtTrfTxInf/RgltryRptg/Dtls.</summary>
public sealed record Pacs008PaymentInitiationDto
{
    /// <summary>OPTIONAL. RgltryRptg/Dtls/Cd (Max10Text): QR, PROXY, MB, IB, NFC, …</summary>
    [JsonPropertyName("channelCode")]
    public string? ChannelCode { get; init; }

    /// <summary>OPTIONAL. RgltryRptg/Dtls/Inf (Max35Text each): payer geolocation; one Inf per item.</summary>
    [JsonPropertyName("geolocation")]
    public IReadOnlyList<string>? Geolocation { get; init; }
}

/// <summary>Annex D §3.2.1.m initiation channel and instrument, carried in CdtTrfTxInf/RltdRmtInf.</summary>
public sealed record Pacs008InitiationChannelInstrumentDto
{
    /// <summary>REQUIRED when the object is present. Exactly 4 uppercase letters.</summary>
    [JsonPropertyName("channelCode")]
    public string? ChannelCode { get; init; }

    /// <summary>
    /// REQUIRED when the object is present, at least one. Each is 4 uppercase letters and produces one
    /// RltdRmtInf/RmtId = channelCode + ":" + instrumentCode (e.g. "MOBL:PRXY").
    /// </summary>
    [JsonPropertyName("instrumentCodes")]
    public IReadOnlyList<string>? InstrumentCodes { get; init; }

    /// <summary>OPTIONAL. RltdRmtInf/RmtLctnDtls/ElctrncAdr (Max2048Text) on every generated RltdRmtInf: POS/device coordinates.</summary>
    [JsonPropertyName("electronicAddress")]
    public string? ElectronicAddress { get; init; }
}

/// <summary>CdtTrfTxInf/RmtInf.</summary>
public sealed record Pacs008RemittanceDto
{
    /// <summary>OPTIONAL. Free text of any length (Ustrd is [0..unbounded]), split into consecutive ≤140-character RmtInf/Ustrd elements.</summary>
    [JsonPropertyName("unstructured")]
    public string? Unstructured { get; init; }

    /// <summary>OPTIONAL. One RmtInf/Strd per item.</summary>
    [JsonPropertyName("structured")]
    public IReadOnlyList<Pacs008StructuredRemittanceDto>? Structured { get; init; }
}

/// <summary>One RmtInf/Strd/CdtrRefInf block.</summary>
public sealed record Pacs008StructuredRemittanceDto
{
    /// <summary>REQUIRED. Strd/CdtrRefInf/Tp/CdOrPrtry/Prtry (Max35Text): MCC, SERV, BILL, RSCUSORDER.</summary>
    [JsonPropertyName("referenceType")]
    public string? ReferenceType { get; init; }

    /// <summary>OPTIONAL. Strd/CdtrRefInf/Tp/Issr (Max35Text).</summary>
    [JsonPropertyName("referenceIssuer")]
    public string? ReferenceIssuer { get; init; }

    /// <summary>REQUIRED. Strd/CdtrRefInf/Ref (Max35Text). For MCC: the 4-digit merchant category code.</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    /// <summary>OPTIONAL. Up to 420 characters, split into ≤140-character Strd/AddtlRmtInf elements (max 3).</summary>
    [JsonPropertyName("additionalInformation")]
    public string? AdditionalInformation { get; init; }
}
