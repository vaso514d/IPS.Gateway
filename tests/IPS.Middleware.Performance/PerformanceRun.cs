using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace IPS.Middleware.Performance;

// The 013 measurement: start the stack at the shipped execution concurrency with signed messages, warm up, measure, drain, then
// check everything the simulators received against everything the generator sent.
internal static class PerformanceRun
{
    // A deployment signs every message, so the measurement includes signing; the simulated IPS does not verify the signature.
    private const bool Signing = true;
    private static readonly TimeSpan DrainPoll = TimeSpan.FromSeconds(1);

    internal static async Task<PerformanceReport> ExecuteAsync(RunOptions options, TextWriter log, CancellationToken cancellationToken)
    {
        var shipped = ServiceSettings.Shipped();
        var concurrency = int.Parse(shipped["Payments:Outgoing:Execution:Concurrency"] ?? "", CultureInfo.InvariantCulture);
        log.WriteLine($"Starting the stack: {options.Instances} API instances at execution concurrency {concurrency}, signing {Signing}.");
        await using var stack = await LoadStack.StartAsync(options.Instances, concurrency, Signing, cancellationToken);
        var connectionString = await stack.ConnectionStringAsync(cancellationToken);
        await DelayIpsAsync(stack.Simulators, options.IpsDelay, cancellationToken);
        var machine = await Machine.DescribeAsync(cancellationToken);
        var logs = await Task.WhenAll(Enumerable.Range(1, options.Instances).Select(number => stack.LogsOfAsync(number, cancellationToken)));
        var startedAtUtc = DateTimeOffset.UtcNow;
        using var stopSampling = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sampling = Readiness.SampleAsync(stack.Probes, stopSampling.Token);
        IReadOnlyList<SentPayment> payments;
        DrainResult drain;
        try
        {
            payments = await LoadGenerator.RunAsync(stack.Instances, Phases(options), log, cancellationToken);
            log.WriteLine($"{DateTimeOffset.UtcNow:HH:mm:ss} Every request answered; draining for up to {options.Drain}.");
            drain = await DrainAsync(connectionString, options.Drain, cancellationToken);
        }
        finally
        {
            await stopSampling.CancelAsync();
        }

        var readiness = await sampling;
        var logSummaries = logs
            .Select(instance => instance.Stop())
            .ToArray();
        log.WriteLine($"{DateTimeOffset.UtcNow:HH:mm:ss} Collecting what the simulators received.");
        var received = await SimulatorRecord.ReadAsync(stack.Simulators, cancellationToken);
        var environments = Enumerable.Range(1, options.Instances)
            .Select(stack.EnvironmentOf)
            .ToArray();
        var configuration = new RunConfiguration(
            options.Instances,
            "projects",
            concurrency,
            Signing,
            options.WarmUpRate,
            options.WarmUp,
            options.Rate,
            options.Duration,
            options.Drain,
            options.IpsDelay,
            Readiness.Interval);
        return PerformanceReport.Create(
            startedAtUtc,
            configuration,
            machine,
            payments,
            received,
            drain,
            readiness,
            logSummaries,
            ServiceSettings.Differences(shipped, environments));
    }

    private static IReadOnlyList<LoadPhase> Phases(RunOptions options) =>
    [
        new(Phase.WarmUp, options.WarmUpRate, options.WarmUp),
        new(Phase.Measured, options.Rate, options.Duration)
    ];

    // Every simulated IPS answer waits this long, so the IPS round trip is not zero.
    private static async Task DelayIpsAsync(HttpClient simulators, TimeSpan delay, CancellationToken cancellationToken)
    {
        using var response = await simulators.PostAsJsonAsync(
            "/_sim/behaviour",
            new { DelayMilliseconds = (int)delay.TotalMilliseconds },
            JsonSerializerOptions.Web,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    // Waits until every payment is final and every callback delivered, or until the limit runs out.
    private static async Task<DrainResult> DrainAsync(string connectionString, TimeSpan limit, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            var backlog = await Backlog.ReadAsync(connectionString, cancellationToken);
            var took = Stopwatch.GetElapsedTime(started);
            if (backlog.Drained || took >= limit)
            {
                return new DrainResult(took, backlog.Drained, backlog);
            }

            await Task.Delay(DrainPoll, cancellationToken);
        }
    }
}
