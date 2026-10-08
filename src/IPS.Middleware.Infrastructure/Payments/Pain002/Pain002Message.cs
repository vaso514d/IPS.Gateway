using System.Xml.Linq;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pain002;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Recalls;

namespace IPS.Middleware.Infrastructure.Payments.Pain002;

// Builds the supported pain.002 profile (IPS v1, Annex D 3.2.12 and 8.1.12): the refusal (RJCT) of one payment initiation,
// with the debtor agent our participant. Child order follows the XSD sequence; null children are omitted.
internal static class Pain002Message
{
    private static readonly XNamespace H = Pacs008Xml.HeaderNamespace;
    private static readonly XNamespace P = Pain002Xml.DocumentNamespace;

    // The refused message's name as the source sends it; Annex D also writes pain.001.001.012 (to be confirmed on IPS test).
    private const string RefusedMessageName = PaymentMessageTypes.Pain001Definition;
    private const string Rejected = "RJCT";

    internal static XElement Build(ValidatedPain002 payment, PaymentMessageContext context, IpsMessageProfile profile) =>
        new("Message",
            new XAttribute(XNamespace.Xmlns + "head", H.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "pain", P.NamespaceName),
            Header(payment, context, profile),
            new XElement(P + "Document",
                new XElement(P + "CstmrPmtStsRpt",
                    GroupHeader(payment, context),
                    new XElement(P + "OrgnlGrpInfAndSts",
                        new XElement(P + "OrgnlMsgId", payment.OriginalMessageId),
                        new XElement(P + "OrgnlMsgNmId", RefusedMessageName),
                        new XElement(P + "GrpSts", Rejected),
                        Reason(payment, includeOriginator: false)),
                    new XElement(P + "OrgnlPmtInfAndSts",
                        new XElement(P + "OrgnlPmtInfId", payment.OriginalPaymentInformationId),
                        new XElement(P + "PmtInfSts", Rejected),
                        Reason(payment, includeOriginator: true)))));

    private static XElement Header(ValidatedPain002 payment, PaymentMessageContext context, IpsMessageProfile profile) =>
        new(H + "AppHdr",
            HeaderParty("Fr", payment.ParticipantBic),
            HeaderParty("To", profile.IpsBic),
            new XElement(H + "BizMsgIdr", context.MessageId),
            new XElement(H + "MsgDefIdr", PaymentMessageTypes.Pain002Definition),
            new XElement(H + "CreDt", RecallXml.Timestamp(context.EnvelopeCreatedAtUtc)));

    private static XElement HeaderParty(string name, string bic) =>
        new(H + name, new XElement(H + "FIId", new XElement(H + "FinInstnId", new XElement(H + "BICFI", bic))));

    // The debtor agent is this participant, the originator of the refusal and the payer's bank.
    private static XElement GroupHeader(ValidatedPain002 payment, PaymentMessageContext context) =>
        new(P + "GrpHdr",
            new XElement(P + "MsgId", context.MessageId),
            new XElement(P + "CreDtTm", RecallXml.Timestamp(payment.CreatedAtUtc ?? context.EnvelopeCreatedAtUtc)),
            new XElement(P + "DbtrAgt", new XElement(P + "FinInstnId", new XElement(P + "BICFI", payment.ParticipantBic))));

    // The payment level requires the originator (Annex D 3.x); its name is optional.
    private static XElement Reason(ValidatedPain002 payment, bool includeOriginator) =>
        new(P + "StsRsnInf",
            includeOriginator ? new XElement(P + "Orgtr", payment.OriginatorName is null ? null : new XElement(P + "Nm", payment.OriginatorName)) : null,
            new XElement(P + "Rsn", new XElement(P + "Cd", payment.ReasonCode)),
            payment.AdditionalInformation is null ? null : new XElement(P + "AddtlInf", payment.AdditionalInformation));
}
