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

    internal static XDocument ValidateReply(string xml) => ValidateMessage(xml, ReplyNamespace);

    internal static void ValidatePacs009(string xml) => ValidateMessage(xml, Pacs009.Pacs009Xml.DocumentNamespace);

    internal static void ValidatePacs004(string xml) => ValidateMessage(xml, Pacs004.Pacs004Xml.DocumentNamespace);

    internal static void ValidateCamt056(string xml) => ValidateMessage(xml, Camt056.Camt056Xml.DocumentNamespace);

    internal static void ValidateInvestigation(string xml) => ValidateMessage(xml, Investigation.Pacs028Xml.DocumentNamespace);

    private static XDocument ValidateMessage(string xml, string documentNamespace)
    {
        using var reader = XmlReader.Create(new StringReader(xml), SafeReader);
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        var root = document.Root;
        if (root?.Name != "Message" || root.Elements().Count() != 2 ||
            root.Elements().First().Name != XName.Get("AppHdr", Pacs008Xml.HeaderNamespace) ||
            root.Elements().Last().Name != XName.Get("Document", documentNamespace))
        {
            throw new XmlSchemaValidationException("Expected one Message with AppHdr followed by Document.");
        }

        foreach (var child in root.Elements())
        {
            new XDocument(new XElement(child)).Validate(Schemas.Value, (_, error) => throw error.Exception);
        }

        return document;
    }

    private static XmlSchemaSet Load()
    {
        var schemas = new XmlSchemaSet { XmlResolver = null };
        foreach (var file in new[] { "head.001.001.03.xsd", "pacs.008.001.12.xsd", "pacs.009.001.11.xsd", "pacs.004.001.13.xsd", "camt.056.001.11.xsd", "pacs.002.001.14.xsd", "pacs.028.001.06.xsd" })
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
