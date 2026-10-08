using System.Text;
using System.Xml;
using IPS.Middleware.Application.Payments.Camt029;
using IPS.Middleware.Infrastructure.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Payments.Camt029;

public sealed class Camt029Xml
{
    public const string DocumentNamespace = "urn:iso:std:iso:20022:tech:xsd:camt.029.001.13";

    public string Build(AcceptedCamt029 accepted, PaymentMessageContext context)
    {
        var output = new StringBuilder();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false }))
        {
            Camt029Message.Build(accepted.Payment, context, accepted.Profile).WriteTo(writer);
        }

        var xml = output.ToString();
        Pacs008Schema.ValidateCamt029(xml);
        return xml;
    }
}
