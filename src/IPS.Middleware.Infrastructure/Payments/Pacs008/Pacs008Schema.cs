using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008;

// Schemas of the pacs.008 exchange: the sent message and its pacs.002 reply.
public static class Pacs008Schema
{
    internal const string ReplyNamespace = "urn:iso:std:iso:20022:tech:xsd:pacs.002.001.14";
    private static readonly Lazy<XmlSchemaSet> Schemas = new(Load);

    // Reader settings for untrusted XML: no DTDs and no external resolution.
    internal static XmlReaderSettings SafeReader => new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };

    public static void Validate(string xml) => ValidateMessage(xml, Pacs008Xml.DocumentNamespace);

    internal static IsoMessage ValidateReply(string xml) => ValidateMessage(xml, ReplyNamespace);

    internal static void ValidatePacs009(string xml) => ValidateMessage(xml, Pacs009.Pacs009Xml.DocumentNamespace);

    internal static void ValidatePacs004(string xml) => ValidateMessage(xml, Pacs004.Pacs004Xml.DocumentNamespace);

    internal static void ValidateCamt056(string xml) => ValidateMessage(xml, Camt056.Camt056Xml.DocumentNamespace);

    internal static void ValidateCamt029(string xml) => ValidateMessage(xml, Camt029.Camt029Xml.DocumentNamespace);

    internal static void ValidateCamt055(string xml) => ValidateMessage(xml, Inbound.Transfers.IncomingCamt055Protocol.DocumentNamespace);

    internal static void ValidatePain001(string xml) => ValidateMessage(xml, Inbound.Transfers.IncomingPain001Protocol.DocumentNamespace);

    internal static void ValidatePain002(string xml) => ValidateMessage(xml, Pain002.Pain002Xml.DocumentNamespace);

    internal static void ValidateInvestigation(string xml) => ValidateMessage(xml, Investigation.Pacs028Xml.DocumentNamespace);

    private static IsoMessage ValidateMessage(string xml, string documentNamespace)
    {
        using var reader = XmlReader.Create(new StringReader(xml), SafeReader);
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        var message = IpsEnvelope.Open(document.Root, documentNamespace)
            ?? throw new XmlSchemaValidationException($"Expected one IPS Message ({IpsEnvelope.Namespace}) with AppHdr followed by the {documentNamespace} business element.");

        foreach (var part in new[] { message.Header, message.Document })
        {
            new XDocument(new XElement(part)).Validate(Schemas.Value, (_, error) => throw error.Exception);
        }

        return message;
    }

    private static XmlSchemaSet Load()
    {
        var schemas = new XmlSchemaSet { XmlResolver = null };
        foreach (var file in new[] { "head.001.001.03.xsd", "pacs.008.001.12.xsd", "pacs.009.001.11.xsd", "pacs.004.001.13.xsd", "camt.056.001.11.xsd", "pain.001.001.12.xsd", "pain.002.001.14.xsd", "camt.029.001.13.xsd", "camt.055.001.12.xsd", "pacs.002.001.14.xsd", "pacs.028.001.06.xsd" })
        {
            using var stream = typeof(Pacs008Schema).Assembly.GetManifestResourceStream(
                "IPS.Middleware.Infrastructure.Payments.Pacs008.Schemas." + file)!;
            using var reader = XmlReader.Create(stream, SafeReader);
            schemas.Add(null, reader);
        }
        schemas.Compile();
        return schemas;
    }
}
