using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008;

public static class Pacs008Schema
{
    private static readonly Lazy<XmlSchemaSet> Schemas = new(Load);

    /// <summary>Reader settings for untrusted XML: no DTDs and no external resolution.</summary>
    internal static XmlReaderSettings SafeReader => new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };

    public static void Validate(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), SafeReader);
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        var root = document.Root;
        if (root?.Name != "Message" || root.Elements().Count() != 2 ||
            root.Elements().First().Name != XName.Get("AppHdr", Pacs008Xml.HeaderNamespace) ||
            root.Elements().Last().Name != XName.Get("Document", Pacs008Xml.DocumentNamespace))
            throw new XmlSchemaValidationException("Expected one Message with AppHdr followed by Document.");
        foreach (var child in root.Elements())
            new XDocument(new XElement(child)).Validate(Schemas.Value, (_, error) => throw error.Exception);
    }

    private static XmlSchemaSet Load()
    {
        var schemas = new XmlSchemaSet { XmlResolver = null };
        foreach (var file in new[] { "head.001.001.03.xsd", "pacs.008.001.12.xsd" })
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
