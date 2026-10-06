using System.Globalization;
using System.Xml.Linq;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Camt056;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Payments.Camt056;

// Builds the supported camt.056 profile (IPS v1, Annex D 3.2.4): a recall of one instant credit transfer, with the
// assigner our participant and the assignee IPS. Child order follows the XSD sequence; null children are omitted.
internal static class Camt056Message
{
    private static readonly XNamespace H = Pacs008Xml.HeaderNamespace;
    private static readonly XNamespace P = Camt056Xml.DocumentNamespace;
    private const string ClearingSystem = "IPS";
    private const string LocalInstrument = "INST";

    // A recall always targets an instant credit transfer.
    private const string RecalledMessageName = PaymentMessageTypes.Pacs008Definition;

    internal static XElement Build(ValidatedCamt056 payment, PaymentMessageContext context, IpsMessageProfile profile) =>
        new("Message",
            new XAttribute(XNamespace.Xmlns + "head", H.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "camt", P.NamespaceName),
            Header(payment, context, profile),
            new XElement(P + "Document",
                new XElement(P + "FIToFIPmtCxlReq",
                    Assignment(payment, context, profile),
                    new XElement(P + "Undrlyg", Transaction(payment, profile)))));

    private static XElement Header(ValidatedCamt056 payment, PaymentMessageContext context, IpsMessageProfile profile) =>
        new(H + "AppHdr",
            HeaderParty("Fr", payment.ParticipantBic),
            HeaderParty("To", profile.IpsBic),
            new XElement(H + "BizMsgIdr", context.MessageId),
            new XElement(H + "MsgDefIdr", PaymentMessageTypes.Camt056Definition),
            new XElement(H + "CreDt", Timestamp(context.EnvelopeCreatedAtUtc)));

    private static XElement HeaderParty(string name, string bic) =>
        new(H + name, new XElement(H + "FIId", new XElement(H + "FinInstnId", new XElement(H + "BICFI", bic))));

    private static XElement Assignment(ValidatedCamt056 payment, PaymentMessageContext context, IpsMessageProfile profile) =>
        new(P + "Assgnmt",
            new XElement(P + "Id", context.MessageId),
            new XElement(P + "Assgnr", AssignmentAgent(payment.ParticipantBic)),
            new XElement(P + "Assgne", AssignmentAgent(profile.IpsBic)),
            new XElement(P + "CreDtTm", Timestamp(payment.AssignmentCreatedAtUtc ?? context.EnvelopeCreatedAtUtc)));

    private static XElement AssignmentAgent(string bic) =>
        new(P + "Agt", new XElement(P + "FinInstnId", new XElement(P + "BICFI", bic)));

    private static XElement Transaction(ValidatedCamt056 payment, IpsMessageProfile profile) =>
        new(P + "TxInf",
            new XElement(P + "CxlId", payment.RecallId),
            new XElement(P + "OrgnlGrpInf",
                new XElement(P + "OrgnlMsgId", payment.OriginalMessageId),
                new XElement(P + "OrgnlMsgNmId", RecalledMessageName)),
            new XElement(P + "OrgnlEndToEndId", payment.OriginalEndToEndId),
            new XElement(P + "OrgnlTxId", payment.OriginalTransactionId),
            new XElement(P + "OrgnlIntrBkSttlmAmt",
                new XAttribute("Ccy", payment.OriginalCurrency),
                payment.OriginalAmount.ToString("0.#####", CultureInfo.InvariantCulture)),
            new XElement(P + "OrgnlIntrBkSttlmDt", Date(payment.OriginalSettlementDate)),
            // The originator is the debtor's participant, which must equal the debtor agent (Annex D 3.2.4.i).
            new XElement(P + "CxlRsnInf",
                new XElement(P + "Orgtr", new XElement(P + "Id", new XElement(P + "OrgId", new XElement(P + "AnyBIC", payment.ParticipantBic)))),
                new XElement(P + "Rsn", new XElement(P + "Cd", payment.ReasonCode))),
            OriginalReference(payment.Original, profile));

    private static XElement OriginalReference(RecalledTransaction original, IpsMessageProfile profile) =>
        new(P + "OrgnlTxRef",
            new XElement(P + "IntrBkSttlmDt", Date(original.SettlementDate)),
            // SettlementInstruction15 requires the settlement method, so it is sent with the clearing system.
            new XElement(P + "SttlmInf",
                new XElement(P + "SttlmMtd", "CLRG"),
                new XElement(P + "ClrSys", new XElement(P + "Cd", ClearingSystem))),
            new XElement(P + "PmtTpInf",
                Code("SvcLvl", profile.ServiceLevelCode),
                Code("LclInstrm", LocalInstrument)),
            Remittance(original.Remittance),
            Ultimate("UltmtDbtr", original.UltimateDebtor),
            Party("Dbtr", original.Debtor),
            Account("DbtrAcct", original.Debtor.Account),
            Agent("DbtrAgt", original.DebtorAgent),
            Agent("CdtrAgt", original.CreditorAgent),
            Party("Cdtr", original.Creditor),
            Account("CdtrAcct", original.Creditor.Account),
            Ultimate("UltmtCdtr", original.UltimateCreditor));

    // Ustrd, then a single structured creditor reference with the SCOR code (Annex D 8.1.5).
    private static XElement? Remittance(RecallRemittance? remittance) =>
        remittance is null
            ? null
            : new(P + "RmtInf",
                Optional("Ustrd", remittance.Unstructured),
                remittance.CreditorReference is not { } reference
                    ? null
                    : new XElement(P + "Strd",
                        new XElement(P + "CdtrRefInf",
                            new XElement(P + "Tp",
                                new XElement(P + "CdOrPrtry", new XElement(P + "Cd", "SCOR")),
                                Optional("Issr", reference.Issuer)),
                            new XElement(P + "Ref", reference.Reference))));

    private static XElement Party(string name, RecallParty party) =>
        new(P + name,
            new XElement(P + "Pty",
                new XElement(P + "Nm", party.Name),
                Address(party.Address),
                PartyIdentification(party.Kind, party.Identifier)));

    private static XElement? Ultimate(string name, RecallUltimateParty? party) =>
        party is null
            ? null
            : new(P + name,
                new XElement(P + "Pty",
                    new XElement(P + "Nm", party.Name),
                    PartyIdentification(party.Kind, party.Identifier)));

    private static XElement? PartyIdentification(PaymentPartyKind? kind, string? identifier) =>
        identifier is null
            ? null
            : new(P + "Id",
                new XElement(P + (kind == PaymentPartyKind.Individual ? "PrvtId" : "OrgId"),
                    new XElement(P + "Othr", new XElement(P + "Id", identifier))));

    private static XElement? Address(RecallAddress? address) =>
        address is null
            ? null
            : new(P + "PstlAdr",
                Optional("StrtNm", address.StreetName),
                Optional("BldgNb", address.BuildingNumber),
                Optional("PstCd", address.PostCode),
                Optional("TwnNm", address.TownName),
                Optional("CtrySubDvsn", address.CountrySubdivision),
                Optional("Ctry", address.Country),
                address.AddressLines.Select(line => new XElement(P + "AdrLine", line)));

    private static XElement Agent(string name, RecallAgent agent) =>
        new(P + name, new XElement(P + "FinInstnId", new XElement(P + "BICFI", agent.Bic), Optional("Nm", agent.Name)));

    private static XElement Account(string name, string iban) =>
        new(P + name, new XElement(P + "Id", new XElement(P + "IBAN", iban)));

    private static XElement Code(string name, string code) => new(P + name, new XElement(P + "Cd", code));

    // new XElement(name, null) would emit an empty element; absent values must be omitted.
    private static XElement? Optional(string name, string? value) => value is null ? null : new(P + name, value);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Timestamp(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
