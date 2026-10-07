using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace IPS.Middleware.Application.Diagnostics;

// The one meter of the service. A host attaches a listener or an exporter; nothing here changes what the service does.
// It is static because System.Diagnostics.Metrics listeners attach to a meter by name, so no workflow resolves it from a container.
public static class PaymentMetrics
{
    public const string MeterName = "IPS.Middleware";

    // Message types come from the wire; anything outside this set is counted as "other" to keep the tags bounded.
    private static readonly HashSet<string> KnownMessageTypes =
    [
        "pacs.008", "pacs.009", "pacs.004", "pacs.002", "pacs.028", "camt.056", "camt.029", "camt.055", "pain.001", "pain.002"
    ];

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> OutgoingStatusChanges = Meter.CreateCounter<long>(
        "ips.outgoing.status_changes", description: "Committed outgoing payment status changes by message type and new status.");
    private static readonly Counter<long> OutgoingOutcomesObserved = Meter.CreateCounter<long>(
        "ips.outgoing.outcomes_observed", description: "Committed outcome reports about an outgoing payment that did not change its state.");
    private static readonly Counter<long> Resends = Meter.CreateCounter<long>(
        "ips.resends", description: "Messages sent again to IPS as a possible duplicate.");
    private static readonly Histogram<double> HttpDuration = Meter.CreateHistogram<double>(
        "ips.http.duration", "s", "Duration of one HTTP exchange by client (IPS, CBS callback, incoming core, Proxy) and result.");
    private static readonly Counter<long> Callbacks = Meter.CreateCounter<long>(
        "ips.cbs.callbacks", description: "Outgoing status callbacks to CBS by result.");
    private static readonly Counter<long> Receipts = Meter.CreateCounter<long>(
        "ips.incoming.receipts", description: "Messages received from IPS by message type, counting redeliveries separately.");
    private static readonly Counter<long> Acknowledgements = Meter.CreateCounter<long>(
        "ips.incoming.acknowledgements", description: "MessageAck calls to IPS by result.");
    private static readonly Counter<long> IncomingRegistered = Meter.CreateCounter<long>(
        "ips.incoming.registered", description: "Committed incoming payments and transfers by kind.");
    private static readonly Counter<long> IncomingCoreEvents = Meter.CreateCounter<long>(
        "ips.incoming.core_events", description: "Committed core-system outcomes of incoming payments and transfers by kind, operation and result.");
    private static readonly Counter<long> ProxyCalls = Meter.CreateCounter<long>(
        "ips.proxy.calls", description: "Proxy Solution management calls by operation and result.");
    private static readonly Counter<long> ValidationRejections = Meter.CreateCounter<long>(
        "ips.validation.rejections", description: "Requests rejected by validation, by operation.");
    private static readonly Counter<long> ComponentErrors = Meter.CreateCounter<long>(
        "ips.errors", description: "Failures that were logged and left to recovery, by component.");

    private static volatile BacklogSnapshot _backlog = BacklogSnapshot.Empty;

    static PaymentMetrics()
    {
        Meter.CreateObservableGauge("ips.backlog.due", () => Backlog.Items.Select(item =>
            new Measurement<long>(item.Count, new KeyValuePair<string, object?>("kind", item.Kind))),
            description: "Work whose next action is due, by kind, from the latest snapshot.");
        Meter.CreateObservableGauge("ips.backlog.oldest_age", () => Backlog.Items.Select(item =>
            new Measurement<double>(item.OldestAgeSeconds, new KeyValuePair<string, object?>("kind", item.Kind))),
            "s", "Age of the oldest due item by kind, at the latest snapshot.");
        Meter.CreateObservableGauge("ips.backlog.snapshot_age", () => Backlog.TakenAtUtc == DateTimeOffset.MinValue
            ? 0
            : (DateTimeOffset.UtcNow - Backlog.TakenAtUtc).TotalSeconds,
            "s", "Seconds since the backlog gauges were last refreshed; a growing value means the snapshot is failing.");
        Meter.CreateObservableGauge("ips.inbound.journal", () => Backlog.Journal.Select(entry =>
            new Measurement<long>(entry.Count, new KeyValuePair<string, object?>("status", entry.Status))),
            description: "Received IPS messages by processing status, from the latest snapshot.");
    }

    public static BacklogSnapshot Backlog => _backlog;

    public static void PublishBacklog(BacklogSnapshot snapshot) => _backlog = snapshot;

    public static void OutgoingStatusChanged(string messageType, string status) =>
        OutgoingStatusChanges.Add(1, Tag("message_type", Known(messageType)), Tag("status", status));

    public static void OutgoingOutcomeObserved(string messageType, bool conflicting) =>
        OutgoingOutcomesObserved.Add(1, Tag("message_type", Known(messageType)), Tag("conflicting", conflicting));

    public static void ResendSent() => Resends.Add(1);

    // result: ok (2xx), http_error, timeout or failed.
    public static void HttpExchanged(string client, TimeSpan duration, string result) =>
        HttpDuration.Record(duration.TotalSeconds, Tag("client", client), Tag("result", result));

    public static void CallbackDelivered(string result) => Callbacks.Add(1, Tag("result", result));

    public static void ReceiptReceived(string messageType, bool redelivery) =>
        Receipts.Add(1, Tag("message_type", Known(messageType)), Tag("redelivery", redelivery));

    public static void Acknowledged(string result) => Acknowledgements.Add(1, Tag("result", result));

    public static void IncomingKindRegistered(string kind) => IncomingRegistered.Add(1, Tag("kind", kind));

    public static void IncomingCoreEvent(string kind, string operation, string result) =>
        IncomingCoreEvents.Add(1, Tag("kind", kind), Tag("operation", operation), Tag("result", result));

    public static void ProxyCalled(string operation, string result) =>
        ProxyCalls.Add(1, Tag("operation", operation), Tag("result", result));

    public static void ValidationRejected(string operation) => ValidationRejections.Add(1, Tag("operation", operation));

    public static void ErrorLogged(string component) => ComponentErrors.Add(1, Tag("component", component));

    // Timing helpers so call sites need no Stopwatch plumbing; the caller reports the result.
    public static long Start() => Stopwatch.GetTimestamp();

    public static TimeSpan Elapsed(long started) => Stopwatch.GetElapsedTime(started);

    // A full definition such as pacs.008.001.12 counts as its short type.
    private static string Known(string messageType)
    {
        var parts = messageType.Split('.');
        var shortType = parts.Length >= 2 ? parts[0] + "." + parts[1] : messageType;
        return KnownMessageTypes.Contains(shortType) ? shortType : "other";
    }

    private static KeyValuePair<string, object?> Tag(string name, object? value) => new(name, value);
}

public sealed record BacklogItem(string Kind, long Count, double OldestAgeSeconds);

public sealed record JournalCount(string Status, long Count);

// What the periodic SQL snapshot saw; the gauges report it until the next one.
public sealed record BacklogSnapshot(DateTimeOffset TakenAtUtc, IReadOnlyList<BacklogItem> Items, IReadOnlyList<JournalCount> Journal)
{
    public static BacklogSnapshot Empty { get; } = new(DateTimeOffset.MinValue, [], []);
}
