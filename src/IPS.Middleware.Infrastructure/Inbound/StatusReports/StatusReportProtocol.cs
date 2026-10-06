using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Xml.Linq;
using IPS.Middleware.Application.Inbound.StatusReports;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Inbound.StatusReports;

// An unsolicited report is judged exactly like the reply to our own send: same schema, signature, identifiers and statuses.
public sealed class StatusReportProtocol(IReadOnlyCollection<X509Certificate2> trustedIpsCertificates) : IStatusReportProtocol
{
    private static readonly XNamespace Pacs002 = Pacs008Schema.ReplyNamespace;

    private readonly IpsReplyInterpreter _interpreter = new(trustedIpsCertificates);

    // Only a lookup key; nothing is trusted until Interpret verifies the signature and identifiers.
    public string? OriginalMessageId(string xml)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), Pacs008Schema.SafeReader);
            var id = XDocument.Load(reader)
                .Descendants(Pacs002 + "OrgnlGrpInfAndSts")
                .Elements(Pacs002 + "OrgnlMsgId")
                .FirstOrDefault()
                ?.Value.Trim();
            return string.IsNullOrEmpty(id) ? null : id;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    public IpsReply Interpret(string xml, IpsReplyCorrelation sent) =>
        _interpreter.Interpret(new IpsSubmissionResponse(200, xml, []), sent);
}
