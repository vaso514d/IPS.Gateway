using System.Diagnostics;
using System.Text.Json;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Payments.Transport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Payments;

public sealed class OutgoingCrashTests
{
    [Theory]
    [InlineData("intake")]
    [InlineData("unsigned")]
    [InlineData("ready")]
    [InlineData("marker")]
    [InlineData("response")]
    [InlineData("outcome")]
    [InlineData("lost-reply")]
    public async Task Killed_process_resumes_only_from_committed_evidence(string checkpoint)
    {
        await using var fixture = await OutgoingHostFixture.CreateAsync(); fixture.LoseReply = checkpoint == "lost-reply";
        await using var db = fixture.Database.Context();
        var transport = new OutgoingTransportSettings
        {
            Enabled = true,
            ParticipantBic = "BAGAGE22",
            Ips = new() { BaseUrl = fixture.Server.Url, RequestTimeout = TimeSpan.FromSeconds(3), ConnectTimeout = TimeSpan.FromMilliseconds(200) },
            Cbs = new() { BaseUrl = fixture.Server.Url },
            SigningCertificate = fixture.Certificates.Identity,
            IpsSignatureTrust = [fixture.Certificates.SignatureTrust]
        };
        var directory = Path.Combine(Path.GetTempPath(), "ips-outgoing-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "probe.json"); var signal = Path.Combine(directory, "checkpoint");
        var settings = new OutgoingProcessProbe.Settings(db.Database.GetConnectionString()!, transport, checkpoint, signal, true);
        try
        {
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(settings));
            using (var first = Start(file))
            {
                try { await WaitAsync(() => Task.FromResult(File.Exists(signal)), first); }
                finally { await KillAsync(first); }
            }
            var originalXml = fixture.Submissions.FirstOrDefault();
            var sends = fixture.Submissions.Count;
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(settings with { Checkpoint = "", Initialize = false }));
            using var resumed = Start(file);
            try
            {
                var expected = checkpoint is "marker" or "lost-reply" ? TransactionStatus.Uncertain : TransactionStatus.Accepted;
                await WaitAsync(() => db.Payments.AsNoTracking().AnyAsync(p => p.CurrentStatus == expected), resumed);
                if (checkpoint is "marker" or "response" or "outcome" or "lost-reply") Assert.Equal(sends, fixture.Submissions.Count);
                else Assert.Single(fixture.Submissions);
                if (originalXml is not null) Assert.Equal(originalXml, Assert.Single(fixture.Submissions));
                if (expected == TransactionStatus.Accepted) await WaitAsync(() => Task.FromResult(fixture.Callbacks.Count > 0), resumed);
                Assert.True(fixture.Submissions.Count <= 1);
            }
            finally { await KillAsync(resumed); }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
    private static Process Start(string file)
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(typeof(OutgoingProcessProbe).Assembly.Location);
        info.ArgumentList.Add("--outgoing-probe"); info.ArgumentList.Add(file);
        return Process.Start(info)!;
    }
    private static async Task KillAsync(Process process)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }
    private static async Task WaitAsync(Func<Task<bool>> ready, Process process)
    {
        var watch = Stopwatch.StartNew();
        while (!await ready())
        {
            Assert.False(process.HasExited, "Outgoing probe exited unexpectedly.");
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(25), "Outgoing checkpoint/recovery timed out.");
            await Task.Delay(25);
        }
    }
}
