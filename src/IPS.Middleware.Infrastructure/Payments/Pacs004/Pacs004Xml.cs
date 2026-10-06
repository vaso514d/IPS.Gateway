using System.Text;
using System.Xml;
using IPS.Middleware.Application.Payments.Pacs004;
using IPS.Middleware.Infrastructure.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Payments.Pacs004;

public sealed class Pacs004Xml
{
    public const string DocumentNamespace = "urn:iso:std:iso:20022:tech:xsd:pacs.004.001.13";

    public string Build(AcceptedPacs004 accepted, PaymentMessageContext context)
    {
        var output = new StringBuilder();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false }))
        {
            Pacs004Message.Build(accepted.Payment, context, accepted.Profile).WriteTo(writer);
        }

        var xml = output.ToString();
        Pacs008Schema.ValidatePacs004(xml);
        return xml;
    }
}
