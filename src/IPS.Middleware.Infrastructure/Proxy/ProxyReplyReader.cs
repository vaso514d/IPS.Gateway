using System.Xml;
using System.Xml.Linq;
using IPS.Middleware.Application.Proxy;
using IPS.Middleware.Infrastructure.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Proxy;

// Reads the pacs.002.001.13 answer to an acmt.022 (Annex E 2.3.5), by element local names because the Proxy Solution's prefixing
// is not guaranteed. The body is the only source of the result; there is no status header.
internal static class ProxyReplyReader
{
    private const string Accepted = "ACCP";

    internal static ProxyOutcome Read(string xml, string operationId)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), Pacs008Schema.SafeReader);
            document = XDocument.Load(reader);
        }
        catch (XmlException error)
        {
            return ProxyOutcome.Reject(ProxyErrorCodes.InternalError, $"Could not parse the Proxy response: {error.Message}");
        }

        var report = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "FIToFIPmtStsRpt");
        if (report is null)
        {
            return ProxyOutcome.Reject(ProxyErrorCodes.InternalError, "Could not parse the Proxy response: it does not contain FIToFIPmtStsRpt.");
        }

        var group = Child(report, "OrgnlGrpInfAndSts");
        var groupStatus = Value(group, "GrpSts");
        if (groupStatus is not null && !IsAccepted(groupStatus))
        {
            var (groupCode, groupDescription) = Reason(Child(group, "StsRsnInf"));
            return ProxyOutcome.Reject(groupCode, groupDescription ?? ProxyErrorCodes.Describe(groupCode));
        }

        var items = report.Elements().Where(element => element.Name.LocalName == "TxInfAndSts").ToArray();
        var item = items.FirstOrDefault(candidate => Value(candidate, "OrgnlTxId") == operationId) ?? items.FirstOrDefault();
        if (item is null)
        {
            return ProxyOutcome.Reject(ProxyErrorCodes.InternalError, "Proxy response contained no matching operation result.");
        }

        if (IsAccepted(Value(item, "TxSts")))
        {
            return ProxyOutcome.Accept();
        }

        var (code, description) = Reason(Child(item, "StsRsnInf"));
        return ProxyOutcome.Reject(code, description ?? ProxyErrorCodes.Describe(code));
    }

    private static bool IsAccepted(string? status) => string.Equals(status, Accepted, StringComparison.OrdinalIgnoreCase);

    private static (string? Code, string? Description) Reason(XElement? information) =>
        (Value(Child(information, "Rsn"), "Cd"), Value(information, "AddtlInf"));

    private static XElement? Child(XElement? parent, string name) => parent?.Elements().FirstOrDefault(element => element.Name.LocalName == name);

    private static string? Value(XElement? parent, string name)
    {
        var value = Child(parent, name)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
