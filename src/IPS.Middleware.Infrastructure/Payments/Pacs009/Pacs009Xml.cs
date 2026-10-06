using System.Text;
using System.Xml;
using IPS.Middleware.Application.Payments.Pacs009;
using IPS.Middleware.Infrastructure.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Payments.Pacs009;

public sealed class Pacs009Xml
{
    public const string DocumentNamespace = "urn:iso:std:iso:20022:tech:xsd:pacs.009.001.11";

    public string Build(AcceptedPacs009 accepted, PaymentMessageContext context)
    {
        var output = new StringBuilder();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false }))
        {
            Pacs009Message.Build(accepted.Payment, context, accepted.Profile).WriteTo(writer);
        }

        var xml = output.ToString();
        Pacs008Schema.ValidatePacs009(xml);
        return xml;
    }
}
