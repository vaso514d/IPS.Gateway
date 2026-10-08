using System.Globalization;

namespace IPS.Middleware.Performance;

internal sealed record RunConfiguration(
    int Instances,
    string ApiRunAs,
    int ExecutionConcurrency,
    bool Signed,
    int WarmUpRate,
    TimeSpan WarmUp,
    int Rate,
    TimeSpan Duration,
    TimeSpan DrainLimit,
    TimeSpan IpsDelay,
    TimeSpan ReadinessInterval);

// Completed is false when the drain limit ran out with payments not final or callbacks pending; Backlog is the last reading.
internal sealed record DrainResult(TimeSpan Took, bool Completed, Backlog Backlog);

internal sealed record Target(string Name, string Limit, string Observed, bool Passed);

// The result of one run against the 013 targets. Latencies and lag cover the measured window; correctness, the per-instance
// breakdown and the timeline cover everything sent.
internal sealed record PerformanceReport(
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    bool Passed,
    IReadOnlyList<Target> Targets,
    Distribution Settlement,
    Distribution Response,
    Distribution Lag,
    Throughput Throughput,
    Correctness Correctness,
    IReadOnlyList<InstanceOutcome> Instances,
    IReadOnlyList<MinuteOutcome> Timeline,
    DrainResult Drain,
    IReadOnlyList<ReadinessSample> Readiness,
    IReadOnlyList<InstanceLogSummary> Logs,
    Machine Machine,
    RunConfiguration Configuration,
    IReadOnlyList<SettingDifference> Settings)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    internal static PerformanceReport Create(
        DateTimeOffset startedAtUtc,
        RunConfiguration configuration,
        Machine machine,
        IReadOnlyList<SentPayment> payments,
        SimulatorRecord received,
        DrainResult drain,
        IReadOnlyList<ReadinessSample> readiness,
        IReadOnlyList<InstanceLogSummary> logs,
        IReadOnlyList<SettingDifference> settings)
    {
        var traffic = IpsTraffic.Of(received.Messages);
        var reports = received.Callbacks
            .Select(CoreReport.Read)
            .OfType<CoreReport>()
            .ToArray();
        var outcomes = PaymentOutcome.Match(payments, traffic, reports);
        var measured = outcomes
            .Where(outcome => outcome.Payment.Phase == Phase.Measured)
            .ToArray();
        var settlement = Distribution.Of(measured
            .Select(outcome => outcome.SettlementLatency)
            .OfType<TimeSpan>());
        var response = Distribution.Of(measured
            .Where(outcome => outcome.Payment.StatusCode is not null)
            .Select(outcome => outcome.Payment.Elapsed));
        var lag = Distribution.Of(measured.Select(outcome => outcome.Payment.Lag));
        var correctness = Correctness.Of(outcomes, traffic, received.Callbacks.Count);
        var targets = Evaluate(settlement, lag, correctness, drain.Backlog, readiness, configuration.Instances);
        return new(
            startedAtUtc,
            DateTimeOffset.UtcNow,
            targets.All(target => target.Passed),
            targets,
            settlement,
            response,
            lag,
            Throughput.Of(outcomes, received, configuration.Rate, configuration.Instances * configuration.ExecutionConcurrency),
            correctness,
            InstanceOutcome.Of(outcomes),
            MinuteOutcome.Of(outcomes),
            drain,
            readiness,
            logs,
            machine,
            configuration,
            settings);
    }

    // The owner's targets (013): settlement p95 and p99; nothing lost, left unsent or duplicated; no 5xx and nothing but 200 or 504;
    // readiness Healthy throughout; a generator that held its rate; and the drain's check that no work is left due.
    private static IReadOnlyList<Target> Evaluate(
        Distribution settlement,
        Distribution lag,
        Correctness correctness,
        Backlog backlog,
        IReadOnlyList<ReadinessSample> readiness,
        int instances)
    {
        var sampledInstances = readiness
            .Select(sample => sample.Instance)
            .Distinct()
            .Count();
        var healthy = readiness.Count(sample => sample.Healthy);
        return
        [
            new("Settlement latency p95", "<= 1000 ms", Milliseconds(settlement.P95, settlement.Samples), settlement.Samples > 0 && settlement.P95 <= 1000),
            new("Settlement latency p99", "<= 2000 ms", Milliseconds(settlement.P99, settlement.Samples), settlement.Samples > 0 && settlement.P99 <= 2000),
            new("Lost payments (accepted, no final callback)", "0", string.Create(Invariant,
                $"{correctness.Lost} (without a callback {correctness.WithoutCallback}, callbacks but none final {correctness.WithoutFinalStatus})"),
                correctness.Lost == 0),
            new("Accepted payments not sent to IPS", "0", string.Create(Invariant,
                $"{correctness.AcceptedNotSent} (never received by the simulated IPS {correctness.NotReceivedByIps}, reported NotSent {correctness.ReportedNotSent})"),
                correctness.AcceptedNotSent == 0),
            new("Duplicate sends", "0", string.Create(Invariant,
                $"{correctness.DuplicateSends} (flagged possible-duplicate resends of the same bytes, allowed: {correctness.FlaggedResends})"),
                correctness.DuplicateSends == 0),
            new("Duplicate callbacks", "0", correctness.DuplicateCallbacks.ToString(Invariant), correctness.DuplicateCallbacks == 0),
            new("5xx responses", "0", string.Create(Invariant,
                $"{correctness.ServerErrors} (504: {correctness.GatewayTimeouts}; requests without a response: {correctness.Unanswered})"),
                correctness.ServerErrors == 0 && correctness.Unanswered == 0),
            new("Every response is 200 or 504", "0 others", string.Create(Invariant,
                $"{correctness.NeitherOkNorTimeout} others ({Describe(correctness.Responses)})"),
                correctness.NeitherOkNorTimeout == 0),
            new("Readiness Healthy throughout", "every sample of every instance", string.Create(Invariant,
                $"{healthy} of {readiness.Count} samples Healthy, {sampledInstances} of {instances} instances sampled"),
                readiness.Count > 0 && healthy == readiness.Count && sampledInstances == instances),
            new("Generator lag p99", "<= 50 ms", Milliseconds(lag.P99, lag.Samples), lag.Samples > 0 && lag.P99 <= 50),
            new("Work due after the drain (ips.backlog.due)", "0", string.Create(Invariant,
                $"{backlog.Due.Sum(work => work.Count)} ({string.Join(", ", backlog.Due.Select(work => $"{work.Kind} {work.Count}"))})"),
                backlog.NothingDue)
        ];
    }

    private static string Describe(IReadOnlyDictionary<string, int> responses) =>
        string.Join(", ", responses.Select(response => string.Create(Invariant, $"{response.Key}: {response.Value}")));

    private static string Milliseconds(double value, int samples) => string.Create(Invariant, $"{value:0.0} ms ({samples} samples)");
}
