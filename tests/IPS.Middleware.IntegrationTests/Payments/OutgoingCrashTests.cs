using System.Diagnostics;
using System.Text.Json;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Payments.Transport;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
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
        await using var fixture = await OutgoingHostFixture.CreateAsync();
        fixture.LoseReply = checkpoint == "lost-reply";
        await using var db = fixture.Database.Context();
        using var files = new ProbeFiles();
        var settings = new OutgoingProcessProbe.Settings(db.Database.GetConnectionString()!, Transport(fixture), checkpoint, files.Signal, true);
        await RunUntilCheckpointAsync(files.Settings, settings);
        var originalXml = fixture.PaymentSubmissions.FirstOrDefault();
        var sends = fixture.PaymentSubmissions.Length;
        await File.WriteAllTextAsync(files.Settings, JsonSerializer.Serialize(settings with
        {
            Checkpoint = "",
            Initialize = false
        }));
        using var resumed = Start(files.Settings);
        try
        {
            var expected = checkpoint is "marker" or "lost-reply" ? TransactionStatus.Uncertain : TransactionStatus.Accepted;
            await WaitAsync(() => db.Payments.AsNoTracking().AnyAsync(p => p.CurrentStatus == expected), resumed);
            if (checkpoint is "marker" or "response" or "outcome" or "lost-reply")
            {
                Assert.Equal(sends, fixture.PaymentSubmissions.Length);
            }
            else
            {
                Assert.Single(fixture.PaymentSubmissions);
            }

            if (originalXml is not null)
            {
                Assert.Equal(originalXml, Assert.Single(fixture.PaymentSubmissions));
            }

            if (expected == TransactionStatus.Accepted)
            {
                await WaitAsync(() => Task.FromResult(fixture.Callbacks.Count > 0), resumed);
            }

            Assert.True(fixture.PaymentSubmissions.Length <= 1);
        }
        finally { await KillAsync(resumed); }
    }

    [Theory]
    [InlineData("resend-ready")]
    [InlineData("resend-marker")]
    [InlineData("resend-response")]
    public async Task Killed_process_sends_an_authorized_resend_at_most_once(string checkpoint)
    {
        await using var fixture = await OutgoingHostFixture.CreateAsync();
        fixture.InvestigationAnswer = "1016";
        await using var db = fixture.Database.Context();
        using var files = new ProbeFiles();
        var settings = new OutgoingProcessProbe.Settings(db.Database.GetConnectionString()!, Transport(fixture), "marker", files.Signal, true);

        // Stopping after the initial marker means IPS truthfully has no record of the payment.
        await RunUntilCheckpointAsync(files.Settings, settings);
        File.Delete(files.Signal);

        // Recovery investigates at once, IPS answers 1016, and the runtime starts the authorized resend.
        await RunUntilCheckpointAsync(files.Settings, settings with { Checkpoint = checkpoint, Initialize = false });
        await File.WriteAllTextAsync(files.Settings, JsonSerializer.Serialize(settings with { Checkpoint = "", Initialize = false }));
        using var resumed = Start(files.Settings);
        try
        {
            await WaitAsync(() => db.Payments.AsNoTracking().AnyAsync(p => p.CurrentStatus == TransactionStatus.Accepted), resumed);
            var original = await db.Set<OutgoingMessageRow>()
                .Where(m => m.InvestigationId == null && m.ResendId == null && m.Direction == OutgoingMessageDirection.Outbound)
                .Select(m => m.Content)
                .SingleAsync();
            Assert.Equal(original, Assert.Single(fixture.PaymentSubmissions));

            // A marked resend is never repeated: its payment is investigated again and resent under a new authorization.
            Assert.Equal(checkpoint == "resend-marker" ? 2 : 1, await db.Set<ResendRow>().CountAsync());
            await WaitAsync(() => Task.FromResult(fixture.Callbacks.Count > 0), resumed);
        }
        finally { await KillAsync(resumed); }
    }

    [Theory]
    [InlineData(PaymentMessageTypes.Pacs009, "ready", false)]
    [InlineData(PaymentMessageTypes.Pacs009, "marker", true)]
    [InlineData(PaymentMessageTypes.Pacs009, "response", false)]
    [InlineData(PaymentMessageTypes.Pacs009, "outcome", false)]
    [InlineData(PaymentMessageTypes.Pacs009, "resend-ready", true)]
    [InlineData(PaymentMessageTypes.Pacs009, "resend-response", true)]
    [InlineData(PaymentMessageTypes.Pacs004, "ready", false)]
    [InlineData(PaymentMessageTypes.Pacs004, "marker", true)]
    [InlineData(PaymentMessageTypes.Pacs004, "response", false)]
    [InlineData(PaymentMessageTypes.Pacs004, "resend-response", true)]
    [InlineData(PaymentMessageTypes.Camt056, "ready", false)]
    [InlineData(PaymentMessageTypes.Camt056, "marker", true)]
    [InlineData(PaymentMessageTypes.Camt056, "response", false)]
    [InlineData(PaymentMessageTypes.Camt056, "resend-response", true)]
    [InlineData(PaymentMessageTypes.Camt029, "ready", false)]
    [InlineData(PaymentMessageTypes.Camt029, "marker", true)]
    [InlineData(PaymentMessageTypes.Camt029, "response", false)]
    [InlineData(PaymentMessageTypes.Camt029, "resend-response", true)]
    public async Task Killed_process_recovers_a_possible_duplicate_message_and_repeats_no_marker(string messageType, string checkpoint, bool resentAsDuplicate)
    {
        await using var fixture = await OutgoingHostFixture.CreateAsync();
        await using var db = fixture.Database.Context();
        using var files = new ProbeFiles();
        var settings = new OutgoingProcessProbe.Settings(db.Database.GetConnectionString()!, Transport(fixture), checkpoint, files.Signal, true, messageType);

        if (checkpoint.StartsWith("resend-", StringComparison.Ordinal))
        {
            // The first process stops after its marker, so IPS has no record; the second authorizes the resend and stops at the checkpoint.
            await RunUntilCheckpointAsync(files.Settings, settings with { Checkpoint = "marker" });
            File.Delete(files.Signal);
            await RunUntilCheckpointAsync(files.Settings, settings with { Initialize = false });
        }
        else
        {
            await RunUntilCheckpointAsync(files.Settings, settings);
        }

        await File.WriteAllTextAsync(files.Settings, JsonSerializer.Serialize(settings with { Checkpoint = "", Initialize = false }));
        using var resumed = Start(files.Settings);
        try
        {
            await WaitAsync(() => db.Payments.AsNoTracking().AnyAsync(p => p.CurrentStatus == TransactionStatus.Accepted), resumed);
            await WaitAsync(() => Task.FromResult(fixture.Callbacks.Count > 0), resumed);

            // IPS sees one message in total: the original, or a single flagged resend when the original never left.
            Assert.Equal([resentAsDuplicate], fixture.PossibleDuplicateFlags);
            Assert.Single(fixture.PaymentSubmissions);
            var original = await db.Set<OutgoingMessageRow>()
                .Where(m => m.InvestigationId == null && m.ResendId == null && m.Direction == OutgoingMessageDirection.Outbound)
                .Select(m => m.Content)
                .SingleAsync();
            Assert.Equal(original, fixture.PaymentSubmissions[0]);
        }
        finally { await KillAsync(resumed); }
    }

    // A temporary probe settings file and checkpoint signal for one test.
    private sealed class ProbeFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "ips-outgoing-probe-" + Guid.NewGuid().ToString("N"));

        public ProbeFiles() => Directory.CreateDirectory(_directory);

        public string Settings => Path.Combine(_directory, "probe.json");
        public string Signal => Path.Combine(_directory, "checkpoint");

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }

    private static OutgoingTransportSettings Transport(OutgoingHostFixture fixture) => new()
    {
        Enabled = true,
        ParticipantBic = "BAGAGE22",
        Ips = new() { BaseUrl = fixture.Server.Url, RequestTimeout = TimeSpan.FromSeconds(3), ConnectTimeout = TimeSpan.FromMilliseconds(200) },
        Cbs = new() { BaseUrl = fixture.Server.Url },
        SigningCertificate = fixture.Certificates.Identity,
        IpsSignatureTrust = [fixture.Certificates.SignatureTrust]
    };

    private static async Task RunUntilCheckpointAsync(string file, OutgoingProcessProbe.Settings settings)
    {
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(settings));
        using var process = Start(file);
        try
        {
            await WaitAsync(() => Task.FromResult(File.Exists(settings.Signal)), process);
        }
        finally { await KillAsync(process); }
    }

    private static Process Start(string file)
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add(typeof(OutgoingProcessProbe).Assembly.Location);
        info.ArgumentList.Add("--outgoing-probe");
        info.ArgumentList.Add(file);
        return Process.Start(info)!;
    }
    private static async Task KillAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

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
