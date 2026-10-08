using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using IPS.MiidleWear.Contracts.Transactions;

namespace IPS.Middleware.Performance;

// What the core was told about a payment, by a callback or by the API's answer: the status with its reason code and IPS internal
// code where present.
internal sealed record ReportedStatus(TransactionStatus Status, string? ReasonCode, int? IpsInternalCode, DateTimeOffset AtUtc)
{
    internal bool IsFinal => Status is TransactionStatus.Accepted
        or TransactionStatus.Rejected
        or TransactionStatus.NotSent
        or TransactionStatus.ManuallyResolved;

    // Such as "NotSent TM01/1015".
    internal string Label => ReasonCode is null && IpsInternalCode is null
        ? Status.ToString()
        : string.Create(CultureInfo.InvariantCulture, $"{Status} {ReasonCode}{(IpsInternalCode is { } code ? "/" + code.ToString(CultureInfo.InvariantCulture) : "")}");
}

// A callback the simulated core received, read in full; null when its body is not a status report.
internal sealed record CoreReport(string ClientReference, ReportedStatus Status)
{
    internal static CoreReport? Read(CoreCallback callback) => StatusReport.Read(callback.Body) is { } status
        ? new CoreReport(status.ClientReference, new ReportedStatus(status.Status, status.ReasonCode, status.IpsInternalCode, callback.ReceivedAtUtc))
        : null;
}

internal static class StatusReport
{
    // A body that is not a status report (an error page, a problem) reads as null, so an odd answer never stops the report.
    internal static TransactionStatusDto? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TransactionStatusDto>(json, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed record MessageIdentity(string Definition, string EndToEndId);

// The simulated IPS's record by message definition (AppHdr/MsgDefIdr): a pacs.008 is a send of the payment named by its end-to-end
// id, a pacs.028 an investigation of the payment named by its original end-to-end id. Anything else, or anything that cannot be
// read, counts as other by its definition.
internal sealed record IpsTraffic(ILookup<string, IpsMessage> Sends, ILookup<string, IpsMessage> Investigations, IReadOnlyDictionary<string, int> Other)
{
    internal const string Pacs008 = "pacs.008";
    internal const string Pacs028 = "pacs.028";

    internal int Identified => Sends.Sum(group => group.Count()) + Investigations.Sum(group => group.Count());

    internal static IpsTraffic Of(IReadOnlyList<IpsMessage> messages)
    {
        var identified = messages
            .Select(message => new { Message = message, Identity = Identify(message.Xml) })
            .ToArray();
        return new(
            identified
                .Where(item => item.Identity.Definition == Pacs008)
                .ToLookup(item => item.Identity.EndToEndId, item => item.Message),
            identified
                .Where(item => item.Identity.Definition == Pacs028)
                .ToLookup(item => item.Identity.EndToEndId, item => item.Message),
            Counts.Of(identified
                .Select(item => item.Identity.Definition)
                .Where(definition => definition is not (Pacs008 or Pacs028))));
    }

    private static MessageIdentity Identify(string xml)
    {
        try
        {
            var elements = XDocument.Parse(xml)
                .Descendants()
                .ToArray();
            var definition = Value(elements, "MsgDefIdr") ?? "no MsgDefIdr";
            if (definition.StartsWith(Pacs008, StringComparison.Ordinal))
            {
                return new(Pacs008, Value(elements, "EndToEndId") ?? "");
            }

            if (definition.StartsWith(Pacs028, StringComparison.Ordinal))
            {
                return new(Pacs028, Value(elements, "OrgnlEndToEndId") ?? "");
            }

            return new(definition, "");
        }
        catch (XmlException)
        {
            return new("unreadable XML", "");
        }
    }

    private static string? Value(IEnumerable<XElement> elements, string name) =>
        elements.FirstOrDefault(element => element.Name.LocalName == name)?.Value;
}

internal static class Problem
{
    internal const string NotAnswered200 = "not answered 200";
    internal const string NotSent = "accepted, not sent";
    internal const string Lost = "lost";
    internal const string SentTwice = "sent twice";
    internal const string ReportedTwice = "reported twice";
    internal const string ReportedTwiceWithoutUnknownOutcome = "reported twice without an unknown delivery outcome";

    internal static readonly string[] All = [NotAnswered200, NotSent, Lost, SentTwice, ReportedTwice, ReportedTwiceWithoutUnknownOutcome];
}

// One sent payment with the API's answer, what the simulated IPS and the simulated core received for it, in receive order, and
// the service's records of its callback deliveries.
internal sealed record PaymentOutcome(
    SentPayment Payment,
    ReportedStatus? Answer,
    IReadOnlyList<IpsMessage> Sends,
    int Investigations,
    IReadOnlyList<ReportedStatus> Callbacks,
    IReadOnlyList<DeliveryRecord> Deliveries)
{
    // 200, or 504 for a payment the service took but had no outcome for within its HTTP wait.
    internal bool Accepted => Payment.StatusCode is 200 or 504;

    internal bool ReachedIps => Sends.Count > 0;

    internal ReportedStatus? Final => Callbacks.FirstOrDefault(callback => callback.IsFinal);

    // The specification wants every accepted payment at the IPS: a payment the service gave up before sending (a final NotSent)
    // or that never got there fails it, even though the core was told.
    internal bool NotSent => Accepted && (!ReachedIps || Final?.Status == TransactionStatus.NotSent);

    // The core never learned a final outcome.
    internal bool Lost => Accepted && Final is null;

    // A second send is allowed only as a resend flagged possible duplicate carrying the bytes of the first.
    internal bool SentTwice => Sends.Count > 1 && !(Sends[0].PossibleDuplicate is false && Sends.Skip(1).All(IsResendOfFirst));

    internal bool Resent => Sends.Count > 1 && !SentTwice;

    internal bool ReportedTwice => Callbacks.Count > 1;

    // Callback delivery is at least once (013a decision 1): a repeat is allowed after an unknown delivery outcome. Each claimed
    // attempt calls the core at most once, so a repeat is explained when the service recorded at least as many attempts as the
    // core received callbacks: every attempt before the last then ended without a recorded delivery (the core may have taken it,
    // the service did not record that). More callbacks than attempts means a call without its own claim, a fencing defect.
    internal bool ReportedTwiceWithoutUnknownOutcome => ReportedTwice && Callbacks.Count > Deliveries.Sum(delivery => delivery.Attempts);

    // From the send to the first callback with a final status: when the core knows the outcome.
    internal TimeSpan? SettlementLatency => Final is { } settled ? settled.AtUtc - Payment.SentAtUtc : null;

    internal IReadOnlyList<string> Problems => Problem.All
        .Where(Has)
        .ToArray();

    // The API's answer as the report shows it: the status code with the body's status, or the first line of anything else.
    internal string AnswerLabel => Payment.StatusCode switch
    {
        null => "no response (" + Payment.Failure + ")",
        200 or 504 => string.Create(CultureInfo.InvariantCulture, $"{Payment.StatusCode} {Answer?.Label ?? "unreadable body"}"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{Payment.StatusCode} {FirstLine(Payment.Body)}")
    };

    internal string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"{Payment.Reference}: sent {Payment.SentAtUtc:HH:mm:ss.fff} to middleware-{Payment.Instance}, answered {AnswerLabel}, " +
        $"IPS sends {Sends.Count} (flagged {Sends.Count(message => message.PossibleDuplicate)}), investigations {Investigations}, " +
        $"callbacks [{string.Join(", ", Callbacks.Select(callback => callback.Label))}], " +
        $"deliveries [{string.Join(", ", Deliveries.Select(Describe))}]");

    private static string Describe(DeliveryRecord delivery) => string.Create(CultureInfo.InvariantCulture,
        $"#{delivery.Sequence} {delivery.State} after {delivery.Attempts} attempts{(delivery.LastFailure is { } failure ? ", last failure: " + failure : "")}");

    internal bool Has(string problem) => problem switch
    {
        Problem.NotAnswered200 => Payment.StatusCode != 200,
        Problem.NotSent => NotSent,
        Problem.Lost => Lost,
        Problem.SentTwice => SentTwice,
        Problem.ReportedTwice => ReportedTwice,
        Problem.ReportedTwiceWithoutUnknownOutcome => ReportedTwiceWithoutUnknownOutcome,
        _ => false
    };

    // Matches sends and investigations by end-to-end id, callbacks and deliveries by client reference; both are unique per payment.
    internal static IReadOnlyList<PaymentOutcome> Match(
        IReadOnlyList<SentPayment> payments,
        IpsTraffic traffic,
        IReadOnlyList<CoreReport> reports,
        ILookup<string, DeliveryRecord> deliveries)
    {
        var callbacks = reports.ToLookup(report => report.ClientReference, report => report.Status);
        return payments
            .Select(payment => new PaymentOutcome(
                payment,
                AnswerOf(payment),
                traffic.Sends[payment.EndToEndId].OrderBy(message => message.ReceivedAtUtc).ToArray(),
                traffic.Investigations[payment.EndToEndId].Count(),
                callbacks[payment.Reference].OrderBy(callback => callback.AtUtc).ToArray(),
                deliveries[payment.Reference].OrderBy(delivery => delivery.Sequence).ToArray()))
            .ToArray();
    }

    private bool IsResendOfFirst(IpsMessage message) => message.PossibleDuplicate && message.Xml == Sends[0].Xml;

    private static ReportedStatus? AnswerOf(SentPayment payment) =>
        payment.StatusCode is 200 or 504 && StatusReport.Read(payment.Body) is { } status
            ? new ReportedStatus(status.Status, status.ReasonCode, status.IpsInternalCode, payment.SentAtUtc + payment.Elapsed)
            : null;

    private static string FirstLine(string? body)
    {
        var line = (body ?? "")
            .Split('\n', 2)[0]
            .Trim();
        return line.Length == 0 ? "(empty body)" : line[..Math.Min(line.Length, 160)];
    }
}

internal sealed record Anomaly(string Reference, IReadOnlyList<string> Problems, string Description);

// The correctness counts over everything sent, warm-up included. Unmatched messages and callbacks belong to no payment the
// generator sent or cannot be read; on the run's fresh stack there should be none. Examples give, for each problem, the first
// payments that have it.
internal sealed record Correctness(
    int Sent,
    IReadOnlyDictionary<string, int> Responses,
    IReadOnlyDictionary<string, int> Answers,
    int Accepted,
    int ServerErrors,
    int GatewayTimeouts,
    int NeitherOkNorTimeout,
    int Unanswered,
    int NotReceivedByIps,
    int ReportedNotSent,
    int AcceptedNotSent,
    int WithoutCallback,
    int WithoutFinalStatus,
    int Lost,
    IReadOnlyDictionary<string, int> FinalStatuses,
    int DuplicateSends,
    int FlaggedResends,
    int DuplicateCallbacks,
    int DuplicateCallbacksWithoutUnknownOutcome,
    int Investigations,
    int InvestigatedPayments,
    IReadOnlyDictionary<string, int> OtherMessages,
    int UnmatchedMessages,
    int UnmatchedCallbacks,
    IReadOnlyList<Anomaly> Examples)
{
    private const int ExamplesPerProblem = 5;

    internal static Correctness Of(IReadOnlyList<PaymentOutcome> outcomes, IpsTraffic traffic, int callbacks) => new(
        outcomes.Count,
        Counts.Of(outcomes.Select(outcome => outcome.Payment.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "no response")),
        Counts.Of(outcomes.Select(outcome => outcome.AnswerLabel)),
        outcomes.Count(outcome => outcome.Accepted),
        outcomes.Count(outcome => outcome.Payment.StatusCode >= 500),
        outcomes.Count(outcome => outcome.Payment.StatusCode == 504),
        outcomes.Count(outcome => !outcome.Accepted),
        outcomes.Count(outcome => outcome.Payment.StatusCode is null),
        outcomes.Count(outcome => outcome.Accepted && !outcome.ReachedIps),
        outcomes.Count(outcome => outcome.Accepted && outcome.Final?.Status == TransactionStatus.NotSent),
        outcomes.Count(outcome => outcome.NotSent),
        outcomes.Count(outcome => outcome.Accepted && outcome.Callbacks.Count == 0),
        outcomes.Count(outcome => outcome.Accepted && outcome.Callbacks.Count > 0 && outcome.Final is null),
        outcomes.Count(outcome => outcome.Lost),
        Counts.Of(outcomes.Select(outcome => outcome.Final?.Label ?? "no final callback")),
        outcomes.Count(outcome => outcome.SentTwice),
        outcomes.Count(outcome => outcome.Resent),
        outcomes.Count(outcome => outcome.ReportedTwice),
        outcomes.Count(outcome => outcome.ReportedTwiceWithoutUnknownOutcome),
        outcomes.Sum(outcome => outcome.Investigations),
        outcomes.Count(outcome => outcome.Investigations > 0),
        traffic.Other,
        traffic.Identified - outcomes.Sum(outcome => outcome.Sends.Count + outcome.Investigations),
        callbacks - outcomes.Sum(outcome => outcome.Callbacks.Count),
        FirstOfEachProblem(outcomes));

    private static IReadOnlyList<Anomaly> FirstOfEachProblem(IReadOnlyList<PaymentOutcome> outcomes) => Problem.All
        .SelectMany(problem => outcomes
            .Where(outcome => outcome.Has(problem))
            .Take(ExamplesPerProblem))
        .DistinctBy(outcome => outcome.Payment.Reference)
        .Select(outcome => new Anomaly(outcome.Payment.Reference, outcome.Problems, outcome.Describe()))
        .ToArray();
}

internal static class Counts
{
    // Most frequent first, so the report's dictionaries read as rankings.
    internal static IReadOnlyDictionary<string, int> Of(IEnumerable<string> keys) => keys
        .GroupBy(key => key, StringComparer.Ordinal)
        .OrderByDescending(group => group.Count())
        .ThenBy(group => group.Key, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
}
