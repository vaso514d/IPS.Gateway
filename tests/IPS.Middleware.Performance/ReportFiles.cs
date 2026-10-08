using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IPS.Middleware.Performance;

// Writes a run's report as <yyyy-MM-dd-HHmm>-<label>.md and .json, named by the start of the load so a later run never
// overwrites an earlier one. The JSON holds every reported value, every readiness sample and every deadlock graph; the raw latency
// samples stay in memory only.
internal static class ReportFiles
{
    private const int TopAnswers = 10;
    private const int DeadlocksShown = 5;
    private const int QueryTextShown = 600;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    // A file, not a web page: "<=" stays readable.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static async Task<IReadOnlyList<string>> WriteAsync(
        PerformanceReport report,
        string directory,
        CancellationToken cancellationToken,
        string label = "baseline")
    {
        Directory.CreateDirectory(directory);
        var name = Path.Combine(directory, report.StartedAtUtc.ToString("yyyy-MM-dd-HHmm", Invariant) + "-" + label);
        await File.WriteAllTextAsync(name + ".md", Markdown(report), cancellationToken);
        await File.WriteAllTextAsync(name + ".json", JsonSerializer.Serialize(report, Json) + "\n", cancellationToken);
        return [name + ".md", name + ".json"];
    }

    internal static string Markdown(PerformanceReport report)
    {
        var text = new StringBuilder();
        text.AppendLine(Invariant, $"# Outgoing payment performance, {report.StartedAtUtc:yyyy-MM-dd HH:mm} UTC");
        text.AppendLine();
        text.AppendLine(Invariant, $"Measured with `tests/IPS.Middleware.Performance` as specified in [013](../specs/013-measured-performance.md), from {report.StartedAtUtc:yyyy-MM-dd HH:mm:ss} to {report.FinishedAtUtc:HH:mm:ss} UTC.");
        text.AppendLine();
        text.AppendLine(report.Passed
            ? "**Verdict: pass.** Every target is met."
            : "**Verdict: fail.** Not met: " + string.Join("; ", report.Targets.Where(target => !target.Passed).Select(target => target.Name)) + ". The evidence is below.");
        text.AppendLine();
        Targets(text, report);
        Latency(text, report);
        Throughput(text, report);
        Correctness(text, report.Correctness);
        Instances(text, report.Instances);
        Timeline(text, report.Timeline);
        Logs(text, report);
        Diagnosis(text, report.Diagnosis);
        Drain(text, report);
        Readiness(text, report);
        Machine(text, report.Machine);
        Configuration(text, report.Configuration, report.Settings);
        return text.ToString();
    }

    private static void Targets(StringBuilder text, PerformanceReport report)
    {
        text.AppendLine("## Targets");
        text.AppendLine();
        text.AppendLine("| Target | Limit | Observed | Result |");
        text.AppendLine("|---|---|---|---|");
        foreach (var target in report.Targets)
        {
            text.AppendLine(Invariant, $"| {target.Name} | {target.Limit} | {target.Observed} | {(target.Passed ? "pass" : "**fail**")} |");
        }

        text.AppendLine();
    }

    private static void Latency(StringBuilder text, PerformanceReport report)
    {
        text.AppendLine("## Latency (measured window, milliseconds)");
        text.AppendLine();
        text.AppendLine("Settlement runs from the generator's send to the simulated core's receipt of the payment's final callback; response from the send to the API's HTTP response; lag from a request's planned start to its actual start. Both ends are read from this machine's clock. Percentiles are exact (nearest rank over every sample); the verdict compares the unrounded values.");
        text.AppendLine();
        text.AppendLine("| | Samples | Min | Mean | p50 | p95 | p99 | Max |");
        text.AppendLine("|---|---|---|---|---|---|---|---|");
        Row(text, "Settlement", report.Settlement);
        Row(text, "Response", report.Response);
        Row(text, "Generator lag", report.Lag);
        text.AppendLine();
    }

    private static void Row(StringBuilder text, string name, Distribution distribution) =>
        text.AppendLine(Invariant,
            $"| {name} | {distribution.Samples} | {distribution.Min:0.0} | {distribution.Mean:0.0} | {distribution.P50:0.0} | {distribution.P95:0.0} | {distribution.P99:0.0} | {distribution.Max:0.0} |");

    private static void Throughput(StringBuilder text, PerformanceReport report)
    {
        var throughput = report.Throughput;
        text.AppendLine("## Throughput");
        text.AppendLine();
        text.AppendLine(Invariant, $"- Planned: {throughput.PlannedRate} requests per second; sent: {throughput.SentPerSecond:0.00} per second; callbacks received by the simulated core: {throughput.CallbacksPerSecond:0.00} per second (over the measured window).");
        text.AppendLine(Invariant, $"- Most requests open at once: {throughput.MaxInFlight}, against {throughput.ExecutionSlots} execution slots ({report.Configuration.Instances} instances at concurrency {report.Configuration.ExecutionConcurrency}).");
        text.AppendLine(throughput.ArrivalsAtFullSlots == 0
            ? "- No request started while the open requests filled every execution slot, so the concurrency limit did not queue payments."
            : string.Create(Invariant, $"- {throughput.ArrivalsAtFullSlots} requests started while at least {throughput.ExecutionSlots} requests were open: their payments may have waited for admission, so the concurrency limit, not the code, can bound their latency (013 risk)."));
        text.AppendLine();
    }

    private static void Correctness(StringBuilder text, Correctness correctness)
    {
        text.AppendLine("## Correctness (everything sent, warm-up included)");
        text.AppendLine();
        text.AppendLine("| Check | Count |");
        text.AppendLine("|---|---|");
        text.AppendLine(Invariant, $"| Payments sent | {correctness.Sent} |");
        text.AppendLine(Invariant, $"| Responses by status code | {Join(correctness.Responses)} |");
        text.AppendLine(Invariant, $"| Accepted by the service (200 or 504) | {correctness.Accepted} |");
        text.AppendLine(Invariant, $"| 5xx responses (504 included) | {correctness.ServerErrors} |");
        text.AppendLine(Invariant, $"| 504 responses | {correctness.GatewayTimeouts} |");
        text.AppendLine(Invariant, $"| Responses other than 200 or 504 (no response included) | {correctness.NeitherOkNorTimeout} |");
        text.AppendLine(Invariant, $"| Requests without a response | {correctness.Unanswered} |");
        text.AppendLine(Invariant, $"| Accepted, not sent: never received by the simulated IPS | {correctness.NotReceivedByIps} |");
        text.AppendLine(Invariant, $"| Accepted, not sent: reported NotSent to the core | {correctness.ReportedNotSent} |");
        text.AppendLine(Invariant, $"| Accepted, not sent (either of the two above) | {correctness.AcceptedNotSent} |");
        text.AppendLine(Invariant, $"| Lost: accepted, without a callback | {correctness.WithoutCallback} |");
        text.AppendLine(Invariant, $"| Lost: accepted, callbacks but none final | {correctness.WithoutFinalStatus} |");
        text.AppendLine(Invariant, $"| Lost (either of the two above) | {correctness.Lost} |");
        text.AppendLine(Invariant, $"| Sent more than once other than as a flagged resend of the same bytes | {correctness.DuplicateSends} |");
        text.AppendLine(Invariant, $"| Flagged possible-duplicate resends of the same bytes (allowed) | {correctness.FlaggedResends} |");
        text.AppendLine(Invariant, $"| More than one callback | {correctness.DuplicateCallbacks} |");
        text.AppendLine(Invariant, $"| More than one callback and more callbacks than recorded delivery attempts (no unknown outcome before the repeat) | {correctness.DuplicateCallbacksWithoutUnknownOutcome} |");
        text.AppendLine(Invariant, $"| Investigations (pacs.028) received by the simulated IPS / payments investigated | {correctness.Investigations} / {correctness.InvestigatedPayments} |");
        text.AppendLine(Invariant, $"| Other IPS messages by definition | {(correctness.OtherMessages.Count == 0 ? "none" : Join(correctness.OtherMessages))} |");
        text.AppendLine(Invariant, $"| IPS messages / callbacks for no payment of the run or unreadable | {correctness.UnmatchedMessages} / {correctness.UnmatchedCallbacks} |");
        text.AppendLine();
        text.AppendLine(Invariant, $"API answers by status code and body (the {TopAnswers} most frequent):");
        text.AppendLine();
        text.AppendLine("| Answer | Count |");
        text.AppendLine("|---|---|");
        foreach (var answer in correctness.Answers.Take(TopAnswers))
        {
            text.AppendLine(Invariant, $"| {Cell(answer.Key)} | {answer.Value} |");
        }

        text.AppendLine();
        text.AppendLine("Final status the core was told, with reason code and IPS internal code:");
        text.AppendLine();
        text.AppendLine("| Final callback | Payments |");
        text.AppendLine("|---|---|");
        foreach (var status in correctness.FinalStatuses)
        {
            text.AppendLine(Invariant, $"| {status.Key} | {status.Value} |");
        }

        text.AppendLine();
        if (correctness.Examples.Count > 0)
        {
            text.AppendLine("Examples, the first payments with each problem:");
            text.AppendLine();
            foreach (var example in correctness.Examples)
            {
                text.AppendLine(Invariant, $"- {string.Join(", ", example.Problems)}: {example.Description}");
            }

            text.AppendLine();
        }
    }

    private static void Instances(StringBuilder text, IReadOnlyList<InstanceOutcome> instances)
    {
        text.AppendLine("## By instance (everything sent)");
        text.AppendLine();
        text.AppendLine("| Instance | Sent | Responses | Accepted, not sent | Lost | More than one callback | First failure (send time) |");
        text.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var instance in instances)
        {
            text.AppendLine(Invariant,
                $"| middleware-{instance.Instance} | {instance.Sent} | {Join(instance.Responses)} | {instance.AcceptedNotSent} | {instance.Lost} | {instance.DuplicateCallbacks} | {(instance.FirstFailureAtUtc is { } failure ? failure.ToString("HH:mm:ss.fff", Invariant) : "none")} |");
        }

        text.AppendLine();
    }

    private static void Timeline(StringBuilder text, IReadOnlyList<MinuteOutcome> timeline)
    {
        text.AppendLine("## Timeline (by send time, one row per minute)");
        text.AppendLine();
        text.AppendLine("| From (UTC) | Phase | Sent | 200 | 504 | Other 5xx | Other | Not sent | More than one callback | Settlement p95 (ms) |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var minute in timeline)
        {
            text.AppendLine(Invariant,
                $"| {minute.FromUtc:HH:mm:ss} | {minute.Phase} | {minute.Sent} | {minute.Ok} | {minute.GatewayTimeouts} | {minute.ServerErrors} | {minute.OtherAnswers} | {minute.NotSent} | {minute.DuplicateCallbacks} | {minute.SettlementP95:0} |");
        }

        text.AppendLine();
    }

    private static void Logs(StringBuilder text, PerformanceReport report)
    {
        text.AppendLine("## API logs during the run");
        text.AppendLine();
        if (!report.Configuration.LogsCollected)
        {
            text.AppendLine("Not collected: the run did not follow the instances' logs (`--CollectLogs`), so they did not load the machine.");
            text.AppendLine();
            return;
        }

        var logs = report.Logs;
        text.AppendLine("Warnings and errors each instance wrote from the start of the load to the end of the drain, from the orchestrator's log stream, by level (lower levels are skipped); then the most frequent by template (category and first message line, numbers and identifiers normalised, at most 80 characters) with the first such entry.");
        text.AppendLine();
        text.AppendLine("| Instance | Entries by level |");
        text.AppendLine("|---|---|");
        foreach (var instance in logs)
        {
            text.AppendLine(Invariant, $"| middleware-{instance.Instance} | {(instance.EntriesByLevel.Count == 0 ? "none" : Join(instance.EntriesByLevel))} |");
        }

        text.AppendLine();
        var templates = logs
            .SelectMany(instance => instance.Top.Select(template => new { instance.Instance, Template = template }))
            .ToArray();
        if (templates.Length == 0)
        {
            text.AppendLine("No warning or error was logged.");
            text.AppendLine();
            return;
        }

        text.AppendLine("| Instance | Level | Count | Template | First entry |");
        text.AppendLine("|---|---|---|---|---|");
        foreach (var entry in templates)
        {
            text.AppendLine(Invariant,
                $"| middleware-{entry.Instance} | {entry.Template.Level} | {entry.Template.Count} | {Cell(entry.Template.Template)} | {Cell(entry.Template.Sample)} |");
        }

        text.AppendLine();
    }

    private static void Diagnosis(StringBuilder text, DatabaseDiagnosis? diagnosis)
    {
        if (diagnosis is null)
        {
            return;
        }

        text.AppendLine("## Database diagnosis");
        text.AppendLine();
        text.AppendLine("Query Store captured every statement from the start of the load to the end of the drain. The costliest statements by total duration, with the plan each ran with most often (operators in tree order, with the table and index read); durations in milliseconds; failed executions were aborted (a command timeout or cancellation) or ended in an error; waits are Query Store's categories.");
        text.AppendLine();
        text.AppendLine("| Query | Executions (failed) | Total | Mean | Max | CPU | Mean logical reads | Waits | Statement | Plan |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var query in diagnosis.Queries)
        {
            var waits = string.Join(", ", query.WaitMilliseconds
                .Take(3)
                .Select(wait => string.Create(Invariant, $"{wait.Key} {wait.Value:0}")));
            text.AppendLine(Invariant,
                $"| {query.QueryId} | {query.Executions} ({query.Failed}) | {query.TotalMilliseconds:0} | {query.MeanMilliseconds:0.0} | {query.MaxMilliseconds:0} | {query.CpuMilliseconds:0} | {query.MeanLogicalReads:0} | {Cell(waits)} | `{Cell(Cut(query.Text, QueryTextShown))}` | {Cell(query.Plan)} |");
        }

        text.AppendLine();
        text.AppendLine("The most SQL connections each client process held open on the database, sampled every 2 s from the start of the load to the end of the drain (a pooled connection stays open while idle; Max Pool Size is 100 by default):");
        text.AppendLine();
        text.AppendLine("| Process | Peak connections |");
        text.AppendLine("|---|---|");
        foreach (var peak in diagnosis.Connections)
        {
            text.AppendLine(Invariant, $"| {peak.Process} | {peak.Peak} |");
        }

        text.AppendLine();
        text.AppendLine("Server waits over the run (the container serves only this database), the largest first:");
        text.AppendLine();
        text.AppendLine("| Wait type | Waits | Milliseconds |");
        text.AppendLine("|---|---|---|");
        foreach (var wait in diagnosis.Waits)
        {
            text.AppendLine(Invariant, $"| {wait.WaitType} | {wait.WaitingTasks} | {wait.WaitMilliseconds:0} |");
        }

        text.AppendLine();
        text.AppendLine(Invariant, $"Deadlocks recorded by the {diagnosis.DeadlockSource} since the load started: {diagnosis.Deadlocks.Count}. The JSON report holds every graph.");
        text.AppendLine();
        foreach (var deadlock in diagnosis.Deadlocks.Take(DeadlocksShown))
        {
            text.AppendLine(Invariant, $"- {deadlock.AtUtc:HH:mm:ss.fff}:");
            foreach (var resource in deadlock.Resources)
            {
                text.AppendLine(Invariant, $"  - {resource.Kind} on {resource.Object} {resource.Index}: owners {resource.Owners}; waiters {resource.Waiters}");
            }

            foreach (var process in deadlock.Processes)
            {
                text.AppendLine(Invariant, $"  - {process.Id}{(process.Victim ? " (victim)" : "")} waits {process.LockMode} on {process.WaitResource}: `{Cell(process.Statement)}`");
            }
        }

        if (diagnosis.Deadlocks.Count > 0)
        {
            text.AppendLine();
        }
    }

    private static string Cut(string value, int length) => value.Length <= length ? value : value[..length] + "...";

    private static void Drain(StringBuilder text, PerformanceReport report)
    {
        var drain = report.Drain;
        text.AppendLine("## Drain and backlog");
        text.AppendLine();
        text.AppendLine(drain.Completed
            ? string.Create(Invariant, $"After the last response, every payment was final and every callback delivered within {drain.Took.TotalSeconds:0.0} s (limit {report.Configuration.DrainLimit.TotalSeconds:0} s).")
            : string.Create(Invariant, $"The drain limit of {report.Configuration.DrainLimit.TotalSeconds:0} s ran out with {drain.Backlog.NotFinalPayments} payments not final and {drain.Backlog.PendingCallbacks} callbacks pending."));
        text.AppendLine();
        text.AppendLine("Work due afterwards, by the query behind the `ips.backlog.due` gauge, read from the database (the service publishes the gauge only in its own process):");
        text.AppendLine();
        text.AppendLine("| Kind | Due |");
        text.AppendLine("|---|---|");
        foreach (var work in drain.Backlog.Due)
        {
            text.AppendLine(Invariant, $"| {work.Kind} | {work.Count} |");
        }

        text.AppendLine();
    }

    private static void Readiness(StringBuilder text, PerformanceReport report)
    {
        text.AppendLine(Invariant, $"## Readiness (`/health/ready` every {report.Configuration.ReadinessInterval.TotalSeconds:0} s from warm-up to the end of the drain)");
        text.AppendLine();
        text.AppendLine("| Instance | Samples | Healthy | First | Last |");
        text.AppendLine("|---|---|---|---|---|");
        var instances = report.Readiness
            .GroupBy(sample => sample.Instance)
            .OrderBy(group => group.Key);
        foreach (var instance in instances)
        {
            text.AppendLine(Invariant,
                $"| middleware-{instance.Key} | {instance.Count()} | {instance.Count(sample => sample.Healthy)} | {instance.Min(sample => sample.AtUtc):HH:mm:ss} | {instance.Max(sample => sample.AtUtc):HH:mm:ss} |");
        }

        text.AppendLine();
        var unhealthy = report.Readiness
            .Where(sample => !sample.Healthy)
            .ToArray();
        if (unhealthy.Length > 0)
        {
            text.AppendLine("Samples that were not Healthy:");
            text.AppendLine();
            foreach (var sample in unhealthy)
            {
                text.AppendLine(Invariant, $"- {sample.AtUtc:HH:mm:ss.fff} middleware-{sample.Instance}: {sample.StatusCode?.ToString(Invariant) ?? "no answer"} {sample.Answer}");
            }

            text.AppendLine();
        }
    }

    private static void Machine(StringBuilder text, Machine machine)
    {
        text.AppendLine("## Machine");
        text.AppendLine();
        text.AppendLine(Invariant, $"- CPU: {machine.Cpu}, {machine.LogicalProcessors} logical processors; memory {machine.MemoryGiB:0.0} GiB.");
        text.AppendLine(Invariant, $"- OS: {machine.OperatingSystem}; {machine.Runtime}.");
        text.AppendLine(Invariant, $"- Docker {machine.DockerVersion}: {machine.DockerCpus} CPUs and {machine.DockerMemoryGiB:0.0} GiB for the containers.");
        text.AppendLine(Invariant, $"- Containers running at the start of the load: {(machine.Containers.Count == 0 ? "none" : string.Join(", ", machine.Containers))}.");
        text.AppendLine("- One machine ran SQL Server, the simulators, the API instances and the generator, so they competed for it (013 risk).");
        text.AppendLine();
    }

    private static void Configuration(StringBuilder text, RunConfiguration configuration, IReadOnlyList<SettingDifference> settings)
    {
        text.AppendLine("## Configuration");
        text.AppendLine();
        text.AppendLine(Invariant, $"- {configuration.Instances} API instances run as {configuration.ApiRunAs}, sharing one SQL Server database; requests round-robin over them.");
        text.AppendLine(Invariant, $"- Warm-up: {configuration.WarmUp.TotalSeconds:0} s at {configuration.WarmUpRate} per second (not measured); measured: {configuration.Duration.TotalSeconds:0} s at {configuration.Rate} per second; drain limit {configuration.DrainLimit.TotalSeconds:0} s.");
        text.AppendLine(Invariant, $"- Simulated IPS: answers ACCP after {configuration.IpsDelay.TotalMilliseconds:0} ms without verifying the service's signature; the simulated core takes every callback at once.");
        text.AppendLine(Invariant, $"- Execution concurrency {configuration.ExecutionConcurrency} per instance (shipped default {configuration.ShippedConcurrency}; the Aspire tests use 4); {(configuration.Timings == "Shipped" ? "the shipped timings of `appsettings.json`" : "the AppHost's test timings, as in 013")}.");
        text.AppendLine(configuration.Signed
            ? "- Outgoing messages signed with a generated ECDSA P-256 key (`Payments:Signing:AllowUnsignedInDevelopment` off)."
            : "- Outgoing messages unsigned (`Payments:Signing:AllowUnsignedInDevelopment` on).");
        text.AppendLine();
        text.AppendLine("Settings the instances ran with that differ from `src/IPS.Middleware.Api/appsettings.json` (all injected by the AppHost as environment):");
        text.AppendLine();
        text.AppendLine("| Setting | appsettings.json | Used |");
        text.AppendLine("|---|---|---|");
        foreach (var setting in settings)
        {
            text.AppendLine(Invariant, $"| `{setting.Key}` | {Code(setting.Shipped)} | {Used(setting.UsedByInstance)} |");
        }
    }

    private static string Join(IReadOnlyDictionary<string, int> counts) =>
        string.Join(", ", counts.Select(count => string.Create(Invariant, $"{count.Key}: {count.Value}")));

    // Free text inside a table cell: a pipe would end the cell.
    private static string Cell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

    // One value when every instance used it, otherwise each instance's value.
    private static string Used(IReadOnlyList<string?> byInstance) => byInstance.Distinct().Count() == 1
        ? Code(byInstance[0])
        : string.Join(", ", byInstance.Select((value, index) => $"middleware-{index + 1} {Code(value)}"));

    private static string Code(string? value) => value switch
    {
        null => "(not set)",
        "" => "(empty)",
        _ => "`" + value + "`"
    };
}
