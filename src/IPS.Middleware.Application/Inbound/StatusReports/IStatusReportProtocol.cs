using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Inbound.StatusReports;

// Reads and verifies a pacs.002 that IPS sent on its own about one of our pacs.008 payments.
public interface IStatusReportProtocol
{
    // The original message id the report names, or null when the content is not a readable report.
    string? OriginalMessageId(string xml);

    IpsReply Interpret(string xml, IpsReplyCorrelation sent);
}
