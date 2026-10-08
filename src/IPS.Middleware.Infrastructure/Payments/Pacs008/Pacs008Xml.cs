using System.Text;
using System.Xml;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Payments.Pacs008;

public sealed class Pacs008Xml(Pacs008ProtocolProfile profile)
{
    public const string HeaderNamespace = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";
    public const string DocumentNamespace = "urn:iso:std:iso:20022:tech:xsd:pacs.008.001.12";

    public string Build(ValidatedPacs008 payment, PaymentMessageContext context)
    {
        var output = new StringBuilder();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false }))
        {
            Pacs008Message.Build(payment, context, profile).WriteTo(writer);
        }

        var xml = output.ToString();
        Pacs008Schema.Validate(xml);
        return xml;
    }
}
