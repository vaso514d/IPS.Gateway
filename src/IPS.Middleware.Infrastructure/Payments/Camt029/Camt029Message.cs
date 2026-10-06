using System.Xml.Linq;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Camt029;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Recalls;

namespace IPS.Middleware.Infrastructure.Payments.Camt029;

// Builds the supported camt.029 profile (IPS v1, Annex D 3.2.5): the negative answer (RJCR) to one recall, with the
// assigner our participant and the assignee IPS. Child order follows the XSD sequence; null children are omitted.
internal static class Camt029Message
{
    private static readonly XNamespace H = Pacs008Xml.HeaderNamespace;
    private static readonly XNamespace P = Camt029Xml.DocumentNamespace;

    // The answer always refuses a recall (Annex D 8.1.6).
    private const string Rejected = "RJCR";

    internal static XElement Build(ValidatedCamt029 payment, PaymentMessageContext context, IpsMessageProfile profile) =>
        new("Message",
            new XAttribute(XNamespace.Xmlns + "head", H.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "camt", P.NamespaceName),
            Header(payment, context, profile),
            new XElement(P + "Document",
                new XElement(P + "RsltnOfInvstgtn",
                    RecallXml.Assignment(P, context.MessageId, payment.ParticipantBic, profile.IpsBic, payment.AssignmentCreatedAtUtc ?? context.EnvelopeCreatedAtUtc),
                    new XElement(P + "Sts", new XElement(P + "Conf", Rejected)),
                    new XElement(P + "CxlDtls", Transaction(payment, profile)))));

    private static XElement Header(ValidatedCamt029 payment, PaymentMessageContext context, IpsMessageProfile profile) =>
        new(H + "AppHdr",
            HeaderParty("Fr", payment.ParticipantBic),
            HeaderParty("To", profile.IpsBic),
            new XElement(H + "BizMsgIdr", context.MessageId),
            new XElement(H + "MsgDefIdr", PaymentMessageTypes.Camt029Definition),
            new XElement(H + "CreDt", RecallXml.Timestamp(context.EnvelopeCreatedAtUtc)));

    private static XElement HeaderParty(string name, string bic) =>
        new(H + name, new XElement(H + "FIId", new XElement(H + "FinInstnId", new XElement(H + "BICFI", bic))));

    private static XElement Transaction(ValidatedCamt029 payment, IpsMessageProfile profile) =>
        new(P + "TxInfAndSts",
            new XElement(P + "CxlStsId", payment.CancellationStatusId),
            new XElement(P + "OrgnlGrpInf",
                new XElement(P + "OrgnlMsgId", payment.OriginalMessageId),
                // The answer always refuses a camt.056.
                new XElement(P + "OrgnlMsgNmId", PaymentMessageTypes.Camt056Definition)),
            new XElement(P + "OrgnlEndToEndId", payment.OriginalEndToEndId),
            new XElement(P + "OrgnlTxId", payment.OriginalTransactionId),
            new XElement(P + "TxCxlSts", Rejected),
            RecallXml.Reason(P, "CxlStsRsnInf", payment.ParticipantBic, payment.ReasonCode, payment.AdditionalInformation),
            RecallXml.OriginalReference(P, payment.Original, profile, RecallXml.Amount(P + "IntrBkSttlmAmt", payment.OriginalAmount, payment.OriginalCurrency)));
}
