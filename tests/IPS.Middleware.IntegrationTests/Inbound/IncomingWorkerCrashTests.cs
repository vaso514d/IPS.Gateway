using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.IntegrationTests.Transactions;
using IPS.Middleware.IntegrationTests.Transport;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

public sealed class IncomingWorkerCrashTests(IncomingReplyFixture fixture) : IClassFixture<IncomingReplyFixture>
{
    [Theory]
    [InlineData("receipt")]
    [InlineData("registration")]
    [InlineData("submission")]
    [InlineData("response")]
    [InlineData("reply")]
    [InlineData("reply-response")]
    [InlineData("reply-send")]
    [InlineData("reply-marker")]
    public async Task Killed_process_resumes_committed_evidence_without_repeating_cbs(string checkpoint)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var deliveries = 0; var submissions = 0; var queries = 0; var reversals = 0;
        var replyXml = new ConcurrentQueue<string>();
        string? remoteSignal = null;
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            if (context.Request.Method == "GET" && context.Request.Path == "/Message")
            {
                if (Interlocked.Increment(ref deliveries) == 1)
                {
                    context.Response.Headers["X-MONTRAN-IPS-MessageSeq"] = "1";
                    context.Response.Headers["X-MONTRAN-IPS-MessageType"] = "pacs.008";
                    await context.Response.WriteAsync(fixture.Input.Signed["valid"]);
                }
                else context.Response.Headers["X-MONTRAN-IPS-ReqSts"] = "EMPTY";
            }
            else if (context.Request.Path == "/api/ips/pacs008/receive")
            {
                Interlocked.Increment(ref submissions);
                await context.Response.WriteAsync("{\"status\":\"ACCP\",\"endToEndId\":\"E2E-1\"}");
            }
            else if (context.Request.Path == "/api/ips/payments/status")
            {
                Interlocked.Increment(ref queries);
                await context.Response.WriteAsync("{\"status\":\"ACCP\",\"endToEndId\":\"E2E-1\"}");
            }
            else if (context.Request.Path == "/api/ips/transactions/status/receive")
            {
                Interlocked.Increment(ref reversals);
                context.Response.StatusCode = 200;
            }
            else if (context.Request.Method == "POST" && context.Request.Path == "/Message")
            {
                replyXml.Enqueue(await new StreamReader(context.Request.Body).ReadToEndAsync(context.RequestAborted));
                if (checkpoint == "reply-send" && replyXml.Count == 1)
                {
                    await File.WriteAllTextAsync(remoteSignal!, "reply-send");
                    await Task.Delay(Timeout.Infinite, context.RequestAborted);
                }
                // A late decision may be RJCT; a contradictory response is durably held, never rewritten.
                context.Response.Headers["X-MONTRAN-IPS-ReqSts"] = "ACCP";
                await context.Response.WriteAsync(fixture.Response("accepted").Body);
            }
            else context.Response.StatusCode = 404;
        });
        using var certificates = new TransportCertificates();
        var transport = new IncomingTransportSettings
        {
            Enabled = true,
            ParticipantBic = "BAGAGE22",
            Ips = new() { BaseUrl = server.Url },
            Cbs = new() { BaseUrl = server.Url },
            SigningCertificate = certificates.SavePfx(fixture.Input.Certificate),
            IpsSignatureTrust = [certificates.SavePublic(fixture.Input.Certificate, "ips.pem")]
        };
        var directory = Path.Combine(Path.GetTempPath(), "ips-worker-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var configuration = Path.Combine(directory, "probe.json"); var signal = Path.Combine(directory, "checkpoint");
        await using var db = database.Context();
        remoteSignal = signal;
        var now = DateTimeOffset.Parse("2026-10-04T12:00:01Z");
        var settings = new WorkerProcessProbe.ProbeSettings(db.Database.GetConnectionString()!, transport, now, checkpoint == "reply-send" ? "" : checkpoint, signal);
        try
        {
            await File.WriteAllTextAsync(configuration, JsonSerializer.Serialize(settings));
            using (var first = Start(configuration))
            {
                try { await WaitAsync(() => Task.FromResult(File.Exists(signal)), first); }
                finally { await KillAsync(first); }
            }
            var sendsBeforeRestart = submissions;
            // Startup discovery must work without any of the killed process's channel contents.
            await File.WriteAllTextAsync(configuration, JsonSerializer.Serialize(settings with { Checkpoint = "", Now = now.AddSeconds(46) }));
            using var resumed = Start(configuration);
            try
            {
                await WaitAsync(async () => await db.Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) AS Value FROM InboundMessageJournal WHERE Status IN (1,2)").SingleAsync() == 1, resumed);
                Assert.True(submissions <= 1);
                if (checkpoint is "submission" or "response" or "reply" or "reply-response" or "reply-send" or "reply-marker") Assert.Equal(sendsBeforeRestart, submissions);
                if (checkpoint is "submission" or "response")
                {
                    // Follow-up survives receipt completion/hold and runs without another payment submission.
                    await WaitAsync(() => Task.FromResult(reversals == 1), resumed);
                }
                if (checkpoint == "submission") Assert.True(queries >= 1);
                if (checkpoint is "response" or "reply" or "reply-response" or "reply-send" or "reply-marker") Assert.Equal(0, queries);
                Assert.NotEmpty(replyXml);
                Assert.Single(replyXml.Distinct(StringComparer.Ordinal));
                var attempts = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM IncomingReplyAttempts").SingleAsync();
                Assert.InRange(attempts, 1, 2);
                if (checkpoint is "reply-response" or "reply-marker") Assert.Single(replyXml);
                if (checkpoint == "reply-marker") Assert.Equal(2, attempts);
                if (checkpoint == "reply-send") { Assert.Equal(2, attempts); Assert.Equal(2, replyXml.Count); }
            }
            finally { await KillAsync(resumed); }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static Process Start(string configuration)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(typeof(WorkerProcessProbe).Assembly.Location);
        start.ArgumentList.Add("--worker-probe"); start.ArgumentList.Add(configuration);
        return Process.Start(start)!;
    }

    private static async Task KillAsync(Process process)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }

    private static async Task WaitAsync(Func<Task<bool>> condition, Process process)
    {
        var watch = Stopwatch.StartNew();
        while (!await condition())
        {
            Assert.False(process.HasExited, "Worker probe exited unexpectedly.");
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), "Worker checkpoint was not reached.");
            await Task.Delay(20);
        }
    }
}
