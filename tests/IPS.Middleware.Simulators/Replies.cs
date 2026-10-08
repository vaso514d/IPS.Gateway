using System.Security;
using System.Xml.Linq;

namespace IPS.Middleware.Simulators;

// The answers of the simulated IPS (a pacs.002 for a pacs.008 and the other messages, a header for a pain.002) and of the
// simulated Proxy Solution (a pacs.002.001.13). Text templates follow Annex D 8.1.8 and Annex E 2.3.5.
internal static class Replies
{
    private const string Pacs002Namespace = "urn:iso:std:iso:20022:tech:xsd:pacs.002.001.14";

    // The identifiers the sent message names, which the service checks the answer against.
    internal sealed record Original(string MessageId, string TransactionId, string EndToEndId, string MessageName);

    internal static Original Read(string xml)
    {
        var document = XDocument.Parse(xml);
        string Value(params string[] names) => document.Descendants().First(element => names.Contains(element.Name.LocalName)).Value;
        return new Original(
            Value("BizMsgIdr"),
            Value("OrgnlTxId", "TxId", "OrgnlPmtInfId"),
            Value("OrgnlEndToEndId", "EndToEndId", "OrgnlPmtInfId"),
            Value("MsgDefIdr"));
    }

    // Unsigned: the service's signer adds the signature, so the header has no Sgntr element yet.
    internal static string Pacs002(Original original, bool accepted)
    {
        var status = accepted ? "ACCP" : "RJCT";
        var reason = accepted ? "" : "<pacs:StsRsnInf><pacs:Rsn><pacs:Cd>AC01</pacs:Cd></pacs:Rsn></pacs:StsRsnInf>";
        return "<Message xmlns:head=\"urn:iso:std:iso:20022:tech:xsd:head.001.001.03\" xmlns:pacs=\"" + Pacs002Namespace + "\">" +
            "<head:AppHdr><head:Fr><head:FIId><head:FinInstnId><head:BICFI>NBGEGE22</head:BICFI></head:FinInstnId></head:FIId></head:Fr>" +
            "<head:To><head:FIId><head:FinInstnId><head:BICFI>BAGAGE22</head:BICFI></head:FinInstnId></head:FIId></head:To>" +
            "<head:BizMsgIdr>IPS-REPLY-1</head:BizMsgIdr><head:MsgDefIdr>pacs.002.001.14</head:MsgDefIdr>" +
            "<head:CreDt>2026-10-04T14:00:01.000Z</head:CreDt></head:AppHdr>" +
            "<pacs:Document><pacs:FIToFIPmtStsRpt><pacs:GrpHdr><pacs:MsgId>IPS-REPLY-1</pacs:MsgId><pacs:CreDtTm>2026-10-04T14:00:01.000Z</pacs:CreDtTm></pacs:GrpHdr>" +
            $"<pacs:OrgnlGrpInfAndSts><pacs:OrgnlMsgId>{Escape(original.MessageId)}</pacs:OrgnlMsgId><pacs:OrgnlMsgNmId>{Escape(original.MessageName)}</pacs:OrgnlMsgNmId>" +
            $"<pacs:GrpSts>{status}</pacs:GrpSts></pacs:OrgnlGrpInfAndSts>" +
            "<pacs:TxInfAndSts><pacs:StsId>IPS-STS-1</pacs:StsId>" +
            $"<pacs:OrgnlEndToEndId>{Escape(original.EndToEndId)}</pacs:OrgnlEndToEndId><pacs:OrgnlTxId>{Escape(original.TransactionId)}</pacs:OrgnlTxId>" +
            $"<pacs:TxSts>{status}</pacs:TxSts>{reason}<pacs:AccptncDtTm>2026-10-04T14:00:01.000Z</pacs:AccptncDtTm></pacs:TxInfAndSts>" +
            "</pacs:FIToFIPmtStsRpt></pacs:Document></Message>";
    }

    // The Proxy Solution answers each operation with a pacs.002.001.13 naming the operation id it was sent.
    internal static string ProxyReply(string operationId, bool accepted) =>
        "<Message xmlns:hdr=\"urn:montran:message.01\"><Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pacs.002.001.13\"><FIToFIPmtStsRpt>" +
        "<OrgnlGrpInfAndSts><GrpSts>ACCP</GrpSts></OrgnlGrpInfAndSts>" +
        $"<TxInfAndSts><OrgnlTxId>{Escape(operationId)}</OrgnlTxId><TxSts>{(accepted ? "ACCP" : "RJCT")}</TxSts>" +
        (accepted ? "" : "<StsRsnInf><Rsn><Cd>AM05</Cd></Rsn><AddtlInf>Duplicate alias</AddtlInf></StsRsnInf>") +
        "</TxInfAndSts></FIToFIPmtStsRpt></Document></Message>";

    internal static string ProxyOperationId(string xml)
    {
        var document = XDocument.Parse(xml);
        return document.Descendants().First(element => element.Name.LocalName == "Mod").Elements().First(element => element.Name.LocalName == "Id").Value;
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? "";
}
