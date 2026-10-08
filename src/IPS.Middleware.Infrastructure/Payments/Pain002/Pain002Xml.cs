using System.Text;
using System.Xml;
using IPS.Middleware.Application.Payments.Pain002;
using IPS.Middleware.Infrastructure.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Payments.Pain002;

public sealed class Pain002Xml
{
    public const string DocumentNamespace = "urn:iso:std:iso:20022:tech:xsd:pain.002.001.14";

    public string Build(AcceptedPain002 accepted, PaymentMessageContext context)
    {
        var output = new StringBuilder();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false }))
        {
            Pain002Message.Build(accepted.Payment, context, accepted.Profile).WriteTo(writer);
        }

        var xml = output.ToString();
        Pacs008Schema.ValidatePain002(xml);
        return xml;
    }
}
