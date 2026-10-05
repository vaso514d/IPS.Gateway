using System.Data.Common;
using System.Text.Json;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Inbound;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Inbound.Workers;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IPS.Middleware.IntegrationTests.Inbound;

// Test-only executable: permits real process termination after selected committed checkpoints.
internal static class WorkerProcessProbe
{
    public static async Task Main(string[] args)
    {
        if (args is not ["--worker-probe", var path]) throw new ArgumentException("Expected worker-probe configuration.");
        var settings = JsonSerializer.Deserialize<ProbeSettings>(await File.ReadAllTextAsync(path))!;
        if (!settings.Connection.Contains("IPS_Middleware_Tests_", StringComparison.Ordinal) ||
            !settings.Connection.Contains("(localdb)", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Probe requires an isolated LocalDB test database.");
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddPersistence(settings.Connection);
        builder.Services.AddDbContext<TransactionDbContext>(options => options.AddInterceptors(new PauseAfterCommit(settings)));
        builder.Services.AddSingleton<TimeProvider>(new Clock(settings.Now));
        builder.Services.AddSingleton(settings.Transport);
        builder.Services.AddSingleton(new IncomingWorkerOptions { Enabled = true, EmptyDelay = TimeSpan.FromMilliseconds(20), ErrorDelay = TimeSpan.FromMilliseconds(20) });
        builder.Services.AddSingleton(new InboundSchedulingOptions(discoveryInterval: TimeSpan.FromMilliseconds(20)));
        builder.Services.AddSingleton(new Pacs008ProtocolProfile("NBGEGE22"));
        builder.Services.AddSingleton(new Pacs008SigningPolicy(false, false));
        builder.Services.AddSingleton<Pacs008MessageSigner>();
        builder.Services.AddIncomingHttpClients(); builder.Services.AddIncomingWorkers();
        using var host = builder.Build();
        await host.RunAsync();
    }

    internal sealed record ProbeSettings(string Connection, IncomingTransportSettings Transport, DateTimeOffset Now, string Checkpoint, string Signal);
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        private readonly long started = global::System.Diagnostics.Stopwatch.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => now + global::System.Diagnostics.Stopwatch.GetElapsedTime(started);
    }

    private sealed class PauseAfterCommit(ProbeSettings settings) : DbTransactionInterceptor
    {
        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (settings.Checkpoint.Length == 0) return;
            var entries = eventData.Context!.ChangeTracker.Entries().ToArray();
            bool Has(string type, string? property = null, bool populated = true) => entries.Any(entry => entry.Metadata.ClrType.Name == type &&
                (property is null || (entry.Property(property).CurrentValue is not null) == populated));
            var reached = settings.Checkpoint switch
            {
                "receipt" => Has("InboundJournalEntry"),
                "registration" => Has("IncomingPayment"),
                "submission" => Has("IncomingCoreCallRow", "CompletionJson", false),
                "response" => Has("IncomingCoreCallRow", "CompletionJson"),
                "reply" => Has("IncomingReplyRow", "MessageXml"),
                "reply-marker" => Has("IncomingReplyAttemptRow", "CompletionJson", false),
                "reply-response" => Has("IncomingReplyAttemptRow", "CompletionJson"),
                _ => false
            };
            if (!reached) return;
            await File.WriteAllTextAsync(settings.Signal, settings.Checkpoint, cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
