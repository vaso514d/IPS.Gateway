using System.Globalization;
using System.Xml.Linq;

namespace IPS.Middleware.Infrastructure.Inbound.Transfers;

// ReqdExctnDt of a payment initiation (008a) and of the cancellation request that names one (012c): a date or a date-time.
// As in the source, a value that is not a plain yyyy-MM-dd date (an xs:date may carry a time zone) is left out rather than
// costing the core the message.
internal static class RequestedExecutionDate
{
    internal static DateOnly? From(XElement? choice)
    {
        if (Child(choice, "Dt") is { } date)
        {
            return DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : null;
        }

        return Child(choice, "DtTm") is { } dateTime
            && DateTimeOffset.TryParse(dateTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? DateOnly.FromDateTime(parsed.DateTime)
                : null;
    }

    private static string? Child(XElement? choice, string name) => choice?.Element(choice.Name.Namespace + name)?.Value.Trim();
}
