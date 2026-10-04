using System.Text;
using System.Xml;
using System.Xml.Serialization;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.XmlModels;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008;

public sealed class Pacs008Xml(Pacs008ProtocolProfile profile)
{
    public const string HeaderNamespace = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";
    public const string DocumentNamespace = "urn:iso:std:iso:20022:tech:xsd:pacs.008.001.12";
    private static readonly XmlSerializer Serializer = new(typeof(MessageXml));

    public string Build(ValidatedPacs008 payment, PaymentMessageContext context)
    {
        var message = Pacs008Mapping.Map(payment, context, profile);
        var xml = Serialize(message);
        Pacs008Schema.Validate(xml);
        return xml;
    }

    private static string Serialize(MessageXml message)
    {
        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add("head", HeaderNamespace);
        namespaces.Add("pacs", DocumentNamespace);
        var output = new StringBuilder();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false }))
            Serializer.Serialize(writer, message, namespaces);
        return output.ToString();
    }
}
