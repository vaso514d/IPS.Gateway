using IPS.Middleware.Performance;
using Microsoft.Extensions.Configuration;

// The measured performance run of 013, test support only: starts the AppHost's stack, sends outgoing pacs.008 payments on a fixed
// schedule and writes a report with the verdict against the owner's targets. It needs Docker. Options, as --Name=value:
//   Rate        requests per second in the measured window (default 50)
//   Duration    length of the measured window (default 00:10:00)
//   WarmUpRate  requests per second during the warm-up (default 10)
//   WarmUp      length of the warm-up, which is not measured (default 00:01:00)
//   Drain       longest wait for the last callbacks after every request was answered (default 00:02:00)
//   Instances   API instances sharing the database (default 2)
//   IpsDelay    delay of every simulated IPS answer (default 00:00:00.100)
//   Output      directory of the report (default docs/performance in the repository)
// The exit code is 0 when every target is met, 1 when the report records a failed target and 2 when the run was stopped (Ctrl+C).
var options = RunOptions.From(new ConfigurationBuilder().AddCommandLine(args).Build());
using var stop = new CancellationTokenSource();
// Ctrl+C stops the run and still disposes the stack, so no container or API process is left behind.
Console.CancelKeyPress += (_, cancel) =>
{
    cancel.Cancel = true;
    stop.Cancel();
};

try
{
    var report = await PerformanceRun.ExecuteAsync(options, Console.Out, stop.Token);
    var files = await ReportFiles.WriteAsync(report, options.Output, stop.Token);
    foreach (var target in report.Targets)
    {
        Console.WriteLine($"{(target.Passed ? "pass" : "FAIL")}  {target.Name}: {target.Observed}");
    }

    Console.WriteLine($"Verdict: {(report.Passed ? "pass" : "fail")}. Report: {string.Join(", ", files)}");
    return report.Passed ? 0 : 1;
}
catch (OperationCanceledException) when (stop.IsCancellationRequested)
{
    Console.WriteLine("Stopped before the report was written; the stack was shut down.");
    return 2;
}
