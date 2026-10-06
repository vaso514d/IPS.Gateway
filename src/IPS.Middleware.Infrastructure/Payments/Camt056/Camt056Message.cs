using System.Xml.Linq;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Camt056;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Recalls;

namespace IPS.Middleware.Infrastructure.Payments.Camt056;

// Builds the supported camt.056 profile (IPS v1, Annex D 3.2.4): a recall of one instant credit transfer, with the
// assigner our participant and the assignee IPS. Child order follows the XSD sequence; null children are omitted.
internal static class Camt056Message
{
    private static readonly XNamespace H = Pacs008Xml.HeaderNamespace;
    private static readonly XNamespace P = Camt056Xml.DocumentNamespace;

    // A recall always targets an instant credit transfer.
    private const string RecalledMessageName = PaymentMessageTypes.Pacs008Definition;

    internal static XElement Build(ValidatedCamt056 payment, PaymentMessageContext context, IpsMessageProfile profile) =>
        new("Message",
            new XAttribute(XNamespace.Xmlns + "head", H.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "camt", P.NamespaceName),
            Header(payment, context, profile),
            new XElement(P + "Document",
                new XElement(P + "FIToFIPmtCxlReq",
                    RecallXml.Assignment(P, context.MessageId, payment.ParticipantBic, profile.IpsBic, payment.AssignmentCreatedAtUtc ?? context.EnvelopeCreatedAtUtc),
                    new XElement(P + "Undrlyg", Transaction(payment, profile)))));

    private static XElement Header(ValidatedCamt056 payment, PaymentMessageContext context, IpsMessageProfile profile) =>
        new(H + "AppHdr",
            HeaderParty("Fr", payment.ParticipantBic),
            HeaderParty("To", profile.IpsBic),
            new XElement(H + "BizMsgIdr", context.MessageId),
            new XElement(H + "MsgDefIdr", PaymentMessageTypes.Camt056Definition),
            new XElement(H + "CreDt", RecallXml.Timestamp(context.EnvelopeCreatedAtUtc)));

    private static XElement HeaderParty(string name, string bic) =>
        new(H + name, new XElement(H + "FIId", new XElement(H + "FinInstnId", new XElement(H + "BICFI", bic))));

    private static XElement Transaction(ValidatedCamt056 payment, IpsMessageProfile profile) =>
        new(P + "TxInf",
            new XElement(P + "CxlId", payment.RecallId),
            new XElement(P + "OrgnlGrpInf",
                new XElement(P + "OrgnlMsgId", payment.OriginalMessageId),
                new XElement(P + "OrgnlMsgNmId", RecalledMessageName)),
            new XElement(P + "OrgnlEndToEndId", payment.OriginalEndToEndId),
            new XElement(P + "OrgnlTxId", payment.OriginalTransactionId),
            RecallXml.Amount(P + "OrgnlIntrBkSttlmAmt", payment.OriginalAmount, payment.OriginalCurrency),
            new XElement(P + "OrgnlIntrBkSttlmDt", RecallXml.Date(payment.OriginalSettlementDate)),
            RecallXml.Reason(P, "CxlRsnInf", payment.ParticipantBic, payment.ReasonCode, additionalInformation: null),
            RecallXml.OriginalReference(P, payment.Original, profile, amount: null));
}
