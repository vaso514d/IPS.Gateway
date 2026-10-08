using System.Globalization;
using System.Xml.Linq;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Payments.Investigation;
// The single-payment investigation profile, built only from frozen payment values and request identity.
public sealed class Pacs028Xml(Pacs008ProtocolProfile profile)
{
    public const string MessageDefinition = "pacs.028.001.06";
    internal const string DocumentNamespace = "urn:iso:std:iso:20022:tech:xsd:" + MessageDefinition;
    private static readonly XNamespace P = DocumentNamespace;
    private static readonly XNamespace H = Pacs008Xml.HeaderNamespace;
    private const string LocalInstrument = "INST";
    public string Build(ValidatedPacs008 payment, IpsReplyCorrelation original, InvestigationMessageContext request)
    {
        var message = new XElement("Message",
            new XAttribute(XNamespace.Xmlns + "head", H.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "pacs", P.NamespaceName),
            Header(payment, request),
            new XElement(P + "Document",
                new XElement(P + "FIToFIPmtStsReq",
                    GroupHeader(payment, request),
                    Transaction(payment, original, request))));
        var xml = message.ToString(SaveOptions.DisableFormatting);
        Pacs008Schema.ValidateInvestigation(xml);
        return xml;
    }

    private XElement Header(ValidatedPacs008 payment, InvestigationMessageContext request) =>
        new(H + "AppHdr",
            Party("Fr", payment.ParticipantBic),
            Party("To", profile.IpsBic),
            new XElement(H + "BizMsgIdr", request.MessageId),
            new XElement(H + "MsgDefIdr", MessageDefinition),
            new XElement(H + "CreDt", Timestamp(request.CreatedAtUtc)));

    private static XElement GroupHeader(ValidatedPacs008 payment, InvestigationMessageContext request) =>
        new(P + "GrpHdr",
            new XElement(P + "MsgId", request.MessageId),
            new XElement(P + "CreDtTm", Timestamp(request.CreatedAtUtc)),
            Agent("InstgAgt", payment.ParticipantBic));

    private XElement Transaction(ValidatedPacs008 payment, IpsReplyCorrelation original, InvestigationMessageContext request) =>
        new(P + "TxInf",
            new XElement(P + "StsReqId", request.StatusRequestId),
            new XElement(P + "OrgnlGrpInf",
                new XElement(P + "OrgnlMsgId", original.MessageId),
                new XElement(P + "OrgnlMsgNmId", Pacs008Message.MessageDefinition)),
            new XElement(P + "OrgnlEndToEndId", original.EndToEndId),
            new XElement(P + "OrgnlTxId", original.TransactionId),
            new XElement(P + "AccptncDtTm", Timestamp(payment.AcceptanceDateTime)),
            new XElement(P + "OrgnlTxRef",
                new XElement(P + "PmtTpInf",
                    new XElement(P + "SvcLvl", new XElement(P + "Cd", profile.ServiceLevelCode)),
                    new XElement(P + "LclInstrm", new XElement(P + "Cd", LocalInstrument))),
                Agent("DbtrAgt", payment.ParticipantBic),
                Agent("CdtrAgt", payment.CreditorAgent.Bic)));

    private static XElement Party(string name, string bic) => new(H + name, new XElement(H + "FIId", new XElement(H + "FinInstnId", new XElement(H + "BICFI", bic))));
    private static XElement Agent(string name, string bic) => new(P + name, new XElement(P + "FinInstnId", new XElement(P + "BICFI", bic)));
    private static string Timestamp(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}

public sealed record InvestigationMessageContext(string MessageId, string StatusRequestId, DateTimeOffset CreatedAtUtc);
