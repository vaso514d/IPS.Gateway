using IPS.Middleware.Performance;
using Xunit;
using Xunit.Abstractions;

namespace IPS.Middleware.AspireTests;

// The 013 harness end to end on a short run: a 15-second warm-up, then 30 seconds at 10 requests per second over two instances,
// checking correctness only. Latency is the full baseline's job; here it would only measure how busy the machine is.
public sealed class PerformanceSmokeTests(ITestOutputHelper output)
{
    [PerformanceFact]
    public async Task A_short_run_over_two_instances_answers_sends_and_settles_every_payment_once_and_stays_ready()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ips-performance-smoke-" + Guid.NewGuid().ToString("N"));
        var options = new RunOptions(
            Rate: 10,
            Duration: TimeSpan.FromSeconds(30),
            WarmUpRate: 10,
            WarmUp: TimeSpan.FromSeconds(15),
            Drain: TimeSpan.FromMinutes(2),
            Instances: 2,
            IpsDelay: TimeSpan.FromMilliseconds(100),
            Output: directory);
        try
        {
            var report = await PerformanceRun.ExecuteAsync(options, TextWriter.Null, CancellationToken.None);
            var files = await ReportFiles.WriteAsync(report, directory, CancellationToken.None);
            var correctness = report.Correctness;
            // At-least-once callback delivery is open for the owner's decision (013 implementation notes): reported, not asserted.
            output.WriteLine($"Payments with more than one callback: {correctness.DuplicateCallbacks}");
            foreach (var example in correctness.Examples)
            {
                output.WriteLine($"{string.Join(", ", example.Problems)}: {example.Description}");
            }

            Assert.Empty(correctness.Examples.Where(example => example.Problems.Any(problem => problem != Problem.ReportedTwice)));
            Assert.Equal(450, correctness.Sent);
            Assert.Equal(new Dictionary<string, int> { ["200"] = 450 }, correctness.Responses);
            Assert.Equal(0, correctness.Lost);
            Assert.Equal(0, correctness.AcceptedNotSent);
            Assert.Equal(0, correctness.DuplicateSends);
            Assert.Equal((0, 0), (correctness.UnmatchedMessages, correctness.UnmatchedCallbacks));
            Assert.True(report.Drain.Completed);
            Assert.True(report.Drain.Backlog.NothingDue);
            Assert.NotEmpty(report.Readiness);
            Assert.All(report.Readiness, sample => Assert.True(sample.Healthy, $"middleware-{sample.Instance} at {sample.AtUtc:O}: {sample.StatusCode} {sample.Answer}"));
            Assert.Equal(new[] { 1, 2 }, report.Readiness.Select(sample => sample.Instance).Distinct().Order());
            Assert.True(report.Configuration.Signed);
            Assert.All(files, file => Assert.True(File.Exists(file), file));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
