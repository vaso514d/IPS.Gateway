using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;

namespace IPS.Middleware.Infrastructure.Inbound.Pacs008;

public sealed class IncomingPacs002Reply(Pacs008ProtocolProfile profile, Pacs008MessageSigner signer)
{
    internal const string MessageDefinition = "pacs.002.001.14";
    private const string BusinessService = "RTP";
    private const string DefaultRejectionReason = "MS03";
    private const int SourceDescriptionLimit = 35;
    private static readonly XNamespace Head = Pacs008Xml.HeaderNamespace;
    private static readonly XNamespace Pacs = Pacs008Schema.ReplyNamespace;

    public Pacs008SigningResult Prepare(
        IncomingPacs008Reference original,
        IncomingReplyDecision decision,
        IncomingReplyContext context,
        string participantBic,
        X509Certificate2? certificate) =>
        signer.PrepareReply(BuildUnsigned(original, decision, context, participantBic), certificate);

    // Context is supplied by durable preparation; this builder never generates identifiers or reads the clock.
    public string BuildUnsigned(
        IncomingPacs008Reference original,
        IncomingReplyDecision decision,
        IncomingReplyContext context,
        string participantBic)
    {
        var status = decision.Accepted ? "ACCP" : "RJCT";
        var header = Header(context, participantBic);
        var report = new XElement(Pacs + "FIToFIPmtStsRpt",
            new XElement(Pacs + "GrpHdr",
                Element("MsgId", context.MessageId),
                Element("CreDtTm", Date(context.CreatedAtUtc)),
                Agent("InstgAgt", participantBic)),
            OriginalGroup(original, decision, status),
            Transaction(original, decision, context, participantBic, status));
        var message = new XElement("Message",
            new XAttribute(XNamespace.Xmlns + "head", Head), new XAttribute(XNamespace.Xmlns + "pacs", Pacs),
            header, new XElement(Pacs + "Document", report));
        var xml = new XDocument(message).ToString(SaveOptions.DisableFormatting);
        Pacs008Schema.ValidateReply(xml);
        return xml;
    }

    private XElement Header(IncomingReplyContext context, string participantBic) =>
        new(Head + "AppHdr",
            HeaderParty("Fr", participantBic),
            HeaderParty("To", profile.IpsBic),
            new XElement(Head + "BizMsgIdr", context.MessageId),
            new XElement(Head + "MsgDefIdr", MessageDefinition),
            new XElement(Head + "BizSvc", BusinessService),
            new XElement(Head + "CreDt", Date(context.CreatedAtUtc)));

    private static XElement OriginalGroup(IncomingPacs008Reference original, IncomingReplyDecision decision, string status) =>
        new XElement(Pacs + "OrgnlGrpInfAndSts",
            Element("OrgnlMsgId", original.GroupMessageId),
            Element("OrgnlMsgNmId", Pacs008Message.MessageDefinition),
            Element("GrpSts", status),
            Reason(decision, originator: null));

    private XElement Transaction(
        IncomingPacs008Reference original, IncomingReplyDecision decision, IncomingReplyContext context, string participantBic, string status) =>
        new XElement(Pacs + "TxInfAndSts",
            Element("StsId", context.StatusId),
            Element("OrgnlEndToEndId", original.EndToEndId),
            Optional("OrgnlTxId", original.TransactionId),
            Element("TxSts", status),
            Reason(decision, originator: participantBic),
            Element("AccptncDtTm", Date(decision.ProcessedAtUtc == default ? context.CreatedAtUtc : decision.ProcessedAtUtc)),
            new XElement(Pacs + "OrgnlTxRef",
                new XElement(Pacs + "PmtTpInf",
                    new XElement(Pacs + "SvcLvl", Element("Cd", original.ServiceLevelCode ?? profile.ServiceLevelCode)),
                    new XElement(Pacs + "LclInstrm", Element("Cd", original.LocalInstrumentCode ?? Pacs008ProtocolProfile.InstantServiceLevel))),
                Agent("DbtrAgt", original.DebtorAgentBic)));

    // The group-level reason has no originator; the transaction-level reason names the replying participant.
    private static XElement? Reason(IncomingReplyDecision decision, string? originator) => decision.Accepted ? null : new(Pacs + "StsRsnInf",
        originator is null ? null : new XElement(Pacs + "Orgtr", new XElement(Pacs + "Id", new XElement(Pacs + "OrgId", Element("AnyBIC", originator)))),
        new XElement(Pacs + "Rsn", Element("Cd", string.IsNullOrWhiteSpace(decision.ReasonCode) ? DefaultRejectionReason : decision.ReasonCode)),
        Optional("AddtlInf", Truncate(decision.Description)));

    private static XElement HeaderParty(string name, string bic) =>
        new(Head + name, new XElement(Head + "FIId", new XElement(Head + "FinInstnId", new XElement(Head + "BICFI", bic))));

    private static XElement? Agent(string name, string? bic) =>
        string.IsNullOrWhiteSpace(bic) ? null : new(Pacs + name, new XElement(Pacs + "FinInstnId", Element("BICFI", bic)));

    private static XElement Element(string name, object value) => new(Pacs + name, value);
    private static XElement? Optional(string name, string? value) => string.IsNullOrWhiteSpace(value) ? null : Element(name, value.Trim());
    private static string? Truncate(string? value) => value?.Trim() is { Length: > SourceDescriptionLimit } text ? text[..SourceDescriptionLimit] : value;
    private static string Date(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
