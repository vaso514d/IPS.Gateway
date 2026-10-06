using System.Globalization;
using System.Xml.Linq;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs004;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Payments.Pacs004;

// Builds the supported pacs.004 profile (IPS v1): a full return of one payment, financial institutions identified by BICFI,
// parties by name and optional identifier, accounts as IBAN only, no UETR, original instruction id or original creation
// time, and no instructed agent. Child order follows the XSD sequence; null children are omitted.
internal static class Pacs004Message
{
    private static readonly XNamespace H = Pacs008Xml.HeaderNamespace;
    private static readonly XNamespace P = Pacs004Xml.DocumentNamespace;
    private const string ClearingSystem = "IPS";
    private const string IndirectClearingSystem = "GE";
    private const string LocalInstrument = "INST";
    private const string ChargeBearer = "SLEV";

    internal static XElement Build(ValidatedPacs004 payment, PaymentMessageContext context, Pacs004ProtocolProfile profile) =>
        new("Message",
            new XAttribute(XNamespace.Xmlns + "head", H.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "pacs", P.NamespaceName),
            Header(payment, context, profile),
            new XElement(P + "Document",
                new XElement(P + "PmtRtr",
                    GroupHeader(payment, context),
                    Transaction(payment, context, profile))));

    private static XElement Header(ValidatedPacs004 payment, PaymentMessageContext context, Pacs004ProtocolProfile profile) =>
        new(H + "AppHdr",
            HeaderParty("Fr", payment.ParticipantBic),
            HeaderParty("To", profile.IpsBic),
            new XElement(H + "BizMsgIdr", context.MessageId),
            new XElement(H + "MsgDefIdr", PaymentMessageTypes.Pacs004Definition),
            new XElement(H + "CreDt", Timestamp(context.EnvelopeCreatedAtUtc)));

    private static XElement HeaderParty(string name, string bic) =>
        new(H + name, new XElement(H + "FIId", new XElement(H + "FinInstnId", new XElement(H + "BICFI", bic))));

    private static XElement GroupHeader(ValidatedPacs004 payment, PaymentMessageContext context) =>
        new(P + "GrpHdr",
            new XElement(P + "MsgId", context.MessageId),
            new XElement(P + "CreDtTm", Timestamp(context.EnvelopeCreatedAtUtc)),
            new XElement(P + "NbOfTxs", 1),
            Amount("TtlRtrdIntrBkSttlmAmt", payment.Amount, payment.Currency),
            new XElement(P + "IntrBkSttlmDt", Date(payment.ValueDate)),
            new XElement(P + "SttlmInf",
                new XElement(P + "SttlmMtd", "CLRG"),
                Code("ClrSys", ClearingSystem)),
            new XElement(P + "InstgAgt", FinancialInstitution(payment.ParticipantBic, null)));

    private static XElement Transaction(ValidatedPacs004 payment, PaymentMessageContext context, Pacs004ProtocolProfile profile)
    {
        var original = payment.Original;
        return new(P + "TxInf",
            new XElement(P + "RtrId", context.MessageId),
            OriginalGroup(original),
            new XElement(P + "OrgnlEndToEndId", original.EndToEndId),
            new XElement(P + "OrgnlTxId", original.TransactionId),
            Amount("OrgnlIntrBkSttlmAmt", original.Amount, original.Currency),
            Amount("RtrdIntrBkSttlmAmt", payment.Amount, payment.Currency),
            new XElement(P + "ChrgBr", ChargeBearer),
            new XElement(P + "RtrRsnInf",
                new XElement(P + "Orgtr", new XElement(P + "Id", new XElement(P + "OrgId", new XElement(P + "AnyBIC", payment.ParticipantBic)))),
                new XElement(P + "Rsn", new XElement(P + "Cd", payment.ReturnReasonCode))),
            new XElement(P + "OrgnlTxRef",
                new XElement(P + "IntrBkSttlmDt", Date(original.ValueDate)),
                new XElement(P + "PmtTpInf",
                    Code("SvcLvl", profile.ServiceLevelCode),
                    Code("LclInstrm", LocalInstrument)),
                Party("Dbtr", payment.Debtor),
                Account("DbtrAcct", payment.Debtor),
                new XElement(P + "DbtrAgt", FinancialInstitution(payment.InstructedAgentBic, null)),
                new XElement(P + "CdtrAgt", FinancialInstitution(payment.ParticipantBic, payment.SenderIndirectParticipant)),
                Party("Cdtr", payment.Creditor),
                Account("CdtrAcct", payment.Creditor)));
    }

    // Only a return that quotes both the original message id and its message name carries the original group.
    private static XElement? OriginalGroup(OriginalPaymentReference original) =>
        original is { MessageId: { } messageId, MessageNameId: { } nameId }
            ? new(P + "OrgnlGrpInf", new XElement(P + "OrgnlMsgId", messageId), new XElement(P + "OrgnlMsgNmId", nameId))
            : null;

    private static XElement FinancialInstitution(string bic, string? indirectParticipant) =>
        new(P + "FinInstnId",
            new XElement(P + "BICFI", bic),
            indirectParticipant is null
                ? null
                : new XElement(P + "ClrSysMmbId", Code("ClrSysId", IndirectClearingSystem), new XElement(P + "MmbId", indirectParticipant)));

    private static XElement Party(string name, ReturnedParty party) =>
        new(P + name, new XElement(P + "Pty", new XElement(P + "Nm", party.Name), PartyIdentification(party)));

    private static XElement? PartyIdentification(ReturnedParty party) =>
        party is { Kind: { } kind, Identifier: { } identifier }
            ? new(P + "Id", new XElement(P + (kind == PaymentPartyKind.Organisation ? "OrgId" : "PrvtId"),
                new XElement(P + "Othr", new XElement(P + "Id", identifier))))
            : null;

    private static XElement Account(string name, ReturnedParty party) =>
        new(P + name, new XElement(P + "Id", new XElement(P + "IBAN", party.Account)));

    private static XElement Amount(string name, decimal amount, string currency) =>
        new(P + name, new XAttribute("Ccy", currency), amount.ToString("0.#####", CultureInfo.InvariantCulture));

    private static XElement Code(string name, string code) => new(P + name, new XElement(P + "Cd", code));

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Timestamp(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
