using System.Text;
using System.Xml;
using IPS.Middleware.Application.Payments.Camt056;
using IPS.Middleware.Infrastructure.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Payments.Camt056;

public sealed class Camt056Xml
{
    public const string DocumentNamespace = "urn:iso:std:iso:20022:tech:xsd:camt.056.001.11";

    public string Build(AcceptedCamt056 accepted, PaymentMessageContext context)
    {
        var output = new StringBuilder();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false }))
        {
            Camt056Message.Build(accepted.Payment, context, accepted.Profile).WriteTo(writer);
        }

        var xml = output.ToString();
        Pacs008Schema.ValidateCamt056(xml);
        return xml;
    }
}
