using System.Globalization;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Payments.Pacs008;

namespace IPS.Middleware.Infrastructure.Payments.Pain002;

// IPS decides a pain.002 in one response header (Annex D, as the source reads it): ACCP means IPS took the refusal and
// forwards it to the PISP, RJCT/<code> means it refused it, anything else leaves the outcome unknown. The body, which is
// a pain.002 again, is neither required nor verified.
internal static class Pain002ReplyInterpreter
{
    private const string Accepted = "ACCP";
    private const string Rejected = "RJCT";

    internal static IpsReply Interpret(IpsSubmissionResponse response)
    {
        var statuses = response.Headers
            .Where(header => string.Equals(header.Name, IpsReplyInterpreter.RequestStatusHeader, StringComparison.OrdinalIgnoreCase))
            .Select(header => header.Value.Trim())
            .Distinct()
            .ToArray();
        if (statuses.Length > 1)
        {
            return Unresolved("IPS returned conflicting request statuses.");
        }

        var requestStatus = statuses.SingleOrDefault();
        if (response.HttpStatusCode != 200)
        {
            return Unresolved($"IPS returned HTTP {response.HttpStatusCode} for the pain.002 (request status: {requestStatus ?? "missing"}).");
        }

        return requestStatus?.Split('/', 2)[0].Trim().ToUpperInvariant() switch
        {
            Accepted => new IpsReply(IpsReplyStatus.Accepted, new PaymentDetails(description: "IPS took the pain.002 refusal and forwards it to the PISP.")),
            Rejected => new IpsReply(IpsReplyStatus.Rejected, new PaymentDetails("NARR", InternalCode(requestStatus), $"IPS rejected the pain.002 ({requestStatus}).")),
            _ => Unresolved($"IPS did not say whether it took the pain.002 (request status: {requestStatus ?? "missing"}).")
        };
    }

    // The number after the slash of RJCT/<code> is IPS's internal error code.
    private static int? InternalCode(string requestStatus) =>
        requestStatus.Split('/') is [_, var code] && int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static IpsReply Unresolved(string description) => new(IpsReplyStatus.Unresolved, new PaymentDetails(description: description));
}
