using System.Globalization;
using System.Xml.Linq;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008;

/// <summary>Builds the supported pacs.008 profile. Child order follows the XSD sequence; null children are omitted.</summary>
internal static class Pacs008Message
{
    private static readonly XNamespace H = Pacs008Xml.HeaderNamespace;
    private static readonly XNamespace P = Pacs008Xml.DocumentNamespace;
    private const int AddressLineLength = 70;
    private const int RemittanceLineLength = 140;
    internal const string MessageDefinition = "pacs.008.001.12";
    private const string ClearingSystem = "IPS";
    private const string LocalInstrument = "INST";
    private const string IndirectClearingSystem = "GE";
    private const string BillIdentificationScheme = "BILL";
    private static readonly TimeSpan SettlementOffset = TimeSpan.FromHours(4);

    internal static XElement Build(ValidatedPacs008 payment, PaymentMessageContext context, Pacs008ProtocolProfile profile) =>
        new("Message",
            new XAttribute(XNamespace.Xmlns + "head", H.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "pacs", P.NamespaceName),
            Header(payment, context, profile),
            new XElement(P + "Document",
                new XElement(P + "FIToFICstmrCdtTrf",
                    GroupHeader(payment, context, profile),
                    Transaction(payment, context, profile))));

    private static XElement Header(ValidatedPacs008 payment, PaymentMessageContext context, Pacs008ProtocolProfile profile) =>
        new(H + "AppHdr",
            HeaderParty("Fr", payment.ParticipantBic),
            HeaderParty("To", profile.IpsBic),
            new XElement(H + "BizMsgIdr", context.MessageId),
            new XElement(H + "MsgDefIdr", MessageDefinition),
            new XElement(H + "CreDt", Timestamp(context.EnvelopeCreatedAtUtc)));

    private static XElement HeaderParty(string name, string bic) =>
        new(H + name, new XElement(H + "FIId", new XElement(H + "FinInstnId", new XElement(H + "BICFI", bic))));

    private static XElement GroupHeader(ValidatedPacs008 payment, PaymentMessageContext context, Pacs008ProtocolProfile profile) =>
        new(P + "GrpHdr",
            new XElement(P + "MsgId", context.MessageId),
            new XElement(P + "CreDtTm", Timestamp(payment.CreationDateTime)),
            new XElement(P + "NbOfTxs", 1),
            Amount("TtlIntrBkSttlmAmt", payment),
            new XElement(P + "IntrBkSttlmDt", payment.AcceptanceDateTime.ToOffset(SettlementOffset)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new XElement(P + "SttlmInf",
                new XElement(P + "SttlmMtd", "CLRG"),
                Code("ClrSys", ClearingSystem)),
            new XElement(P + "PmtTpInf",
                new XElement(P + "InstrPrty", payment.Priority == PaymentPriority.High ? "HIGH" : "NORM"),
                Code("SvcLvl", profile.ServiceLevelCode),
                Code("LclInstrm", LocalInstrument),
                Code("CtgyPurp", payment.CategoryPurposeCode)),
            Agent("InstgAgt", new(payment.ParticipantBic, null)));

    private static XElement Transaction(ValidatedPacs008 payment, PaymentMessageContext context, Pacs008ProtocolProfile profile) =>
        new(P + "CdtTrfTxInf",
            new XElement(P + "PmtId",
                new XElement(P + "InstrId", payment.InstructionId),
                new XElement(P + "EndToEndId", payment.EndToEndId),
                new XElement(P + "TxId", context.TransactionId)),
            Amount("IntrBkSttlmAmt", payment),
            new XElement(P + "AccptncDtTm", Timestamp(payment.AcceptanceDateTime)),
            new XElement(P + "ChrgBr", "SLEV"),
            Party("UltmtDbtr", payment.UltimateDebtor),
            Party("Dbtr", payment.Debtor),
            Account("DbtrAcct", payment.DebtorAccount),
            Agent("DbtrAgt", payment.DebtorAgent),
            Agent("CdtrAgt", payment.CreditorAgent),
            Party("Cdtr", payment.Creditor),
            Account("CdtrAcct", payment.CreditorAccount),
            Party("UltmtCdtr", payment.UltimateCreditor),
            Regulatory(payment.PaymentInitiation),
            RelatedRemittance(payment.InitiationChannel, profile.RemittanceMethod),
            Remittance(payment.Remittance));

    private static XElement? Party(string name, PaymentParty? party) => party is null ? null :
        new(P + name,
            new XElement(P + "Nm", party.Name),
            Address(party.Address),
            PartyIdentification(party));

    private static XElement? PartyIdentification(PaymentParty party)
    {
        var identifiers = new[]
        {
            party.Identifier is { } id ? new XElement(P + "Othr", new XElement(P + "Id", id)) : null,
            party.BillIdentifier is { } bill ? new XElement(P + "Othr", new XElement(P + "Id", bill),
                Code("SchmeNm", BillIdentificationScheme)) : null
        }.OfType<XElement>().ToArray();
        if (identifiers.Length == 0) return null;
        return new(P + "Id", new XElement(P + (party.Kind == PaymentPartyKind.Organisation ? "OrgId" : "PrvtId"), identifiers));
    }

    private static XElement? Address(PaymentAddress? address)
    {
        if (address is null) return null;
        // Source profile splits raw address text before trimming individual lines.
        var lines = Chunks(address.AddressLines, AddressLineLength)
            .Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => new XElement(P + "AdrLine", line.Trim()));
        var element = new XElement(P + "PstlAdr",
            Optional("StrtNm", address.StreetName),
            Optional("BldgNb", address.BuildingNumber),
            Optional("PstCd", address.PostCode),
            Optional("TwnNm", address.TownName),
            Optional("CtrySubDvsn", address.CountrySubdivision),
            Optional("Ctry", address.Country),
            lines);
        return element.HasElements ? element : null;
    }

    private static XElement Account(string name, PaymentAccount account) =>
        new(P + name, new XElement(P + "Id", account.Kind == PaymentAccountKind.Treasury
            ? new XElement(P + "Othr", new XElement(P + "Id", account.Value))
            : new XElement(P + "IBAN", account.Value)));

    private static XElement Agent(string name, PaymentAgent agent) =>
        new(P + name, new XElement(P + "FinInstnId",
            new XElement(P + "BICFI", agent.Bic),
            agent.IndirectParticipant is { } member
                ? new XElement(P + "ClrSysMmbId",
                    Code("ClrSysId", IndirectClearingSystem),
                    new XElement(P + "MmbId", member))
                : null));

    private static XElement? Regulatory(PaymentInitiation? initiation) =>
        initiation is null || (initiation.ChannelCode is null && initiation.Geolocation.Count == 0) ? null :
            new(P + "RgltryRptg", new XElement(P + "Dtls",
                Optional("Cd", initiation.ChannelCode),
                initiation.Geolocation.Select(value => new XElement(P + "Inf", value))));

    private static IEnumerable<XElement> RelatedRemittance(PaymentInitiationChannel? channel, RemittanceDeliveryMethod method) =>
        channel is null ? [] : channel.InstrumentCodes.Select(instrument => new XElement(P + "RltdRmtInf",
            new XElement(P + "RmtId", channel.ChannelCode + ":" + instrument),
            channel.ElectronicAddress is { } address
                ? new XElement(P + "RmtLctnDtls",
                    new XElement(P + "Mtd", Code(method)),
                    new XElement(P + "ElctrncAdr", address))
                : null));

    private static XElement? Remittance(PaymentRemittance? remittance) =>
        remittance is null || (remittance.Unstructured is null && remittance.Structured.Count == 0) ? null :
            new(P + "RmtInf",
                Chunks(remittance.Unstructured, RemittanceLineLength).Select(line => new XElement(P + "Ustrd", line)),
                remittance.Structured.Select(reference => new XElement(P + "Strd",
                    new XElement(P + "CdtrRefInf",
                        new XElement(P + "Tp",
                            new XElement(P + "CdOrPrtry", new XElement(P + "Prtry", reference.Type)),
                            Optional("Issr", reference.Issuer)),
                        new XElement(P + "Ref", reference.Reference)),
                    Chunks(reference.AdditionalInformation, RemittanceLineLength)
                        .Select(line => new XElement(P + "AddtlRmtInf", line)))));

    private static XElement Amount(string name, ValidatedPacs008 payment) =>
        new(P + name, new XAttribute("Ccy", payment.Currency), payment.Amount.ToString("0.#####", CultureInfo.InvariantCulture));

    private static XElement? Code(string name, string? code) => code is null ? null : new(P + name, new XElement(P + "Cd", code));

    // new XElement(name, null) would emit an empty element; absent values must be omitted.
    private static XElement? Optional(string name, string? value) => value is null ? null : new(P + name, value);

    private static string Timestamp(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static string Code(RemittanceDeliveryMethod method) => method switch
    {
        RemittanceDeliveryMethod.Fax => "FAXI",
        RemittanceDeliveryMethod.ElectronicDataInterchange => "EDIC",
        RemittanceDeliveryMethod.Uri => "URID",
        RemittanceDeliveryMethod.Email => "EMAL",
        RemittanceDeliveryMethod.Post => "POST",
        RemittanceDeliveryMethod.Sms => "SMSM",
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

    private static IEnumerable<string> Chunks(string? text, int length)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        for (var offset = 0; offset < text.Length; offset += length)
            yield return text.Substring(offset, Math.Min(length, text.Length - offset));
    }
}
