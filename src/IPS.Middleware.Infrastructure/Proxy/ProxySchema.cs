using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using IPS.Middleware.Infrastructure.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Proxy;

// Validates the message the Proxy Solution receives: the hdr:Message wrapper with one application header and one acmt.022.
internal static class ProxySchema
{
    private static readonly Lazy<XmlSchemaSet> Schemas = new(Load);

    internal static void Validate(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), Pacs008Schema.SafeReader);
        var root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root;
        if (root?.Name != XName.Get("Message", ProxyXml.WrapperNamespace) || root.Elements().Count() != 2 ||
            root.Elements().First().Name != XName.Get("AppHdr", ProxyXml.HeaderNamespace) ||
            root.Elements().Last().Name != XName.Get("Document", ProxyXml.DocumentNamespace))
        {
            throw new XmlSchemaValidationException("Expected one Message with AppHdr followed by Document.");
        }

        foreach (var child in root.Elements())
        {
            new XDocument(new XElement(child)).Validate(Schemas.Value, (_, error) => throw error.Exception);
        }
    }

    private static XmlSchemaSet Load()
    {
        var schemas = new XmlSchemaSet { XmlResolver = null };
        Add(schemas, typeof(Pacs008Schema).Assembly, "IPS.Middleware.Infrastructure.Payments.Pacs008.Schemas.head.001.001.03.xsd");
        Add(schemas, typeof(ProxySchema).Assembly, "IPS.Middleware.Infrastructure.Proxy.Schemas.acmt.022.001.04.xsd");
        schemas.Compile();
        return schemas;
    }

    private static void Add(XmlSchemaSet schemas, System.Reflection.Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = XmlReader.Create(stream, Pacs008Schema.SafeReader);
        schemas.Add(null, reader);
    }
}
