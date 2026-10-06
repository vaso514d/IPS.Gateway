using System.Collections.Concurrent;
using System.Diagnostics;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Replies;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Inbound;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Inbound.Workers;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Repositories.Inbound;
using IPS.Middleware.Infrastructure.UnitOfWork;
using IPS.Middleware.IntegrationTests.Transactions;
using IPS.Middleware.IntegrationTests.Transport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

public sealed class IncomingWorkerSqlTests(IncomingReplyFixture fixture) : IClassFixture<IncomingReplyFixture>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Two_hosts_receive_duplicates_and_share_one_cbs_submission_with_distinct_replies(bool acceptReplies)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var time = new Clock();
        var received = new ConcurrentQueue<(long Sequence, string Type, string Xml)>([
            (1, "pacs.008", fixture.Input.Signed["valid"]), (2, "pacs.008", fixture.Input.Signed["alternative"]),
            (1, "pacs.008", fixture.Input.Signed["valid"]), (1, "pacs.008", fixture.Input.Signed["valid"]),
            (0, "pacs.008", "held raw XML"), (3, "camt.056", "unsupported raw XML")]);
        var paths = new ConcurrentQueue<string>();
        var replies = new ConcurrentQueue<string>();
        var submissions = 0;
        var coreStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var coreRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            paths.Enqueue(context.Request.Method + " " + context.Request.Path);
            if (context.Request.Method == "GET" && context.Request.Path == "/Message")
            {
                if (received.TryDequeue(out var message))
                {
                    context.Response.Headers["X-MONTRAN-IPS-MessageSeq"] = message.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    context.Response.Headers["X-MONTRAN-IPS-MessageType"] = message.Type;
                    await context.Response.WriteAsync(message.Xml);
                }
                else
                {
                    context.Response.Headers["X-MONTRAN-IPS-ReqSts"] = "EMPTY";
                }
            }
            else if (context.Request.Path == "/api/ips/pacs008/receive")
            {
                Assert.Equal("E2E-1", context.Request.Headers["Idempotency-Key"]);
                Interlocked.Increment(ref submissions);
                coreStarted.TrySetResult();
                await coreRelease.Task.WaitAsync(context.RequestAborted);
                await context.Response.WriteAsync("{\"status\":\"ACCP\",\"endToEndId\":\"E2E-1\"}");
            }
            else if (context.Request.Method == "POST" && context.Request.Path == "/Message")
            {
                var xml = await new StreamReader(context.Request.Body).ReadToEndAsync(context.RequestAborted);
                replies.Enqueue(xml);
                if (!acceptReplies)
                {
                    context.Response.StatusCode = 503;
                    return;
                }
                var response = fixture.Response(xml.Contains("IN-GROUP-2", StringComparison.Ordinal) ? "alternative" : "accepted");
                context.Response.Headers["X-MONTRAN-IPS-ReqSts"] = "ACCP";
                await context.Response.WriteAsync(response.Body);
            }
            else
            {
                context.Response.StatusCode = 404;
            }
        });
        using var certificates = new TransportCertificates();
        using var one = Host(database, server.Url, certificates, time);
        using var two = Host(database, server.Url, certificates, time);
        await one.StartAsync();
        await two.StartAsync();
        try
        {
            await coreStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await EventuallyAsync(async () =>
            {
                await using var db = database.Context();
                return await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM InboundMessageJournal").SingleAsync() == 4;
            });
            coreRelease.TrySetResult();
            await EventuallyAsync(async () =>
            {
                await using var db = database.Context();
                return await db.Set<IPS.Middleware.Domain.Inbound.IncomingPayment>().AnyAsync(p => p.IpsAccepted == true);
            });
            time.Advance(TimeSpan.FromSeconds(2));
            if (!acceptReplies)
            {
                await EventuallyAsync(async () =>
                {
                    await using var pending = database.Context();
                    return await pending.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM InboundMessageJournal r WHERE r.ClaimToken IS NULL AND EXISTS (SELECT 1 FROM IncomingReplyAttempts a WHERE a.JournalId = r.Id AND a.CompletionJson IS NOT NULL)").SingleAsync() == 2;
                });
                time.Advance(TimeSpan.FromSeconds(1));
            }
            await EventuallyAsync(async () =>
            {
                await using var db = database.Context();
                return await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM InboundMessageJournal WHERE Sequence IN (1,2) AND Status IN (1,2)").SingleAsync() == 2;
            });
            await using var final = database.Context();
            Assert.Equal(1, submissions);
            Assert.Equal(acceptReplies ? 2 : 4, replies.Count);
            if (!acceptReplies)
            {
                Assert.All(replies.GroupBy(xml => xml), group => Assert.Equal(2, group.Count()));
            }

            Assert.Single(await final.Set<IPS.Middleware.Domain.Inbound.IncomingPayment>().ToListAsync());
            Assert.Equal(2, await final.Database.SqlQueryRaw<long>("SELECT DuplicateCount AS Value FROM InboundMessageJournal WHERE Sequence = 1").SingleAsync());
            Assert.Equal(acceptReplies ? 2 : 4, await final.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM InboundMessageJournal WHERE Status = 2").SingleAsync());
            Assert.Contains(replies, xml => xml.Contains("IN-GROUP-1", StringComparison.Ordinal));
            Assert.Contains(replies, xml => xml.Contains("IN-GROUP-2", StringComparison.Ordinal));
            Assert.DoesNotContain(paths, path => path.Contains("Ack", StringComparison.OrdinalIgnoreCase));
        }
        finally { coreRelease.TrySetResult(); await Task.WhenAll(one.StopAsync(), two.StopAsync()); }
    }

    [Fact]
    public async Task Discovery_separates_unsent_replies_from_processing_and_recovers_lost_notifications()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var time = new Clock();
        await using var db = database.Context();
        var receipts = new InboundReceiptRepository(db);
        var a = await receipts.StageRegistrationAsync(new("BAGAGE22", 1, "pacs.008", fixture.Input.Signed["valid"], false, time.GetUtcNow()), default);
        var b = await receipts.StageRegistrationAsync(new("BAGAGE22", 2, "pacs.008", fixture.Input.Signed["valid"], false, time.GetUtcNow().AddTicks(1)), default);
        await new UnitOfWork(db).SaveAsync();
        var work = new InboundWorkRepository(db);
        var claim = (await work.StageClaimAsync(a.JournalId, time.GetUtcNow(), TimeSpan.FromSeconds(45), default))!;
        await new UnitOfWork(db).SaveAsync();
        var original = ((IPS.Middleware.Application.Inbound.Pacs008.IncomingPacs008ReadResult.Ready)
            new IPS.Middleware.Infrastructure.Inbound.Pacs008.IncomingPacs008Reader().Read(fixture.Input.Signed["valid"], [fixture.Input.Certificate])).Payment.Original;
        await work.StageOriginalReferencesAsync(claim, original, time.GetUtcNow(), default);
        await new IncomingReplyRepository(db).StageEnvelopeAsync(claim, new("BAGAGE22", original,
            new(false, time.GetUtcNow(), "FF01", "Invalid structure"), new("HEADER", "GROUP", time.GetUtcNow()), new("NBGEGE22"), 2), time.GetUtcNow(), default);
        await work.StageFinishAsync(claim, time.GetUtcNow(), time.GetUtcNow(), default);
        await new UnitOfWork(db).SaveAsync();
        time.Advance(TimeSpan.FromMilliseconds(1));
        var services = new ServiceCollection();
        services.AddPersistence(db.Database.GetConnectionString()!);
        services.AddSingleton<TimeProvider>(time);
        services.AddInboundFoundations(new(capacity: 1, discoveryBatch: 1));
        services.AddSingleton<InboundReplyChannel>();
        services.AddSingleton<InboundDispatchDiscovery>();
        await using var provider = services.BuildServiceProvider();
        var discovery = provider.GetRequiredService<InboundDispatchDiscovery>();
        var processing = provider.GetRequiredService<InboundProcessingChannel>();
        var reply = provider.GetRequiredService<InboundReplyChannel>();
        await discovery.RefillAsync(false, default);
        await discovery.RefillAsync(true, default);
        Assert.True(processing.TryRead(out var processingId));
        Assert.Equal(b.JournalId, processingId);
        Assert.True(reply.TryRead(out var replyId));
        Assert.Equal(a.JournalId, replyId);
        Assert.False(processing.TryRead(out _));
        Assert.False(reply.TryRead(out _));
        // Throw away every in-memory notification; SQL reconstructs both routes.
        await discovery.RefillAsync(false, default);
        await discovery.RefillAsync(true, default);
        Assert.True(processing.TryRead(out processingId));
        Assert.Equal(b.JournalId, processingId);
        Assert.True(reply.TryRead(out replyId));
        Assert.Equal(a.JournalId, replyId);
    }

    [Fact]
    public async Task Receive_preserves_whitespace_retries_failed_commit_and_publishes_only_after_commit()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var db = database.Context();
        var failure = new FailedReceiptSave();
        var receiver = new ScriptedReceiver();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(db.Database.GetConnectionString()!);
        services.AddDbContext<IPS.Middleware.Infrastructure.Transactions.TransactionDbContext>(o => o.AddInterceptors(failure));
        services.AddSingleton<IIncomingReceiveClient>(receiver);
        services.AddSingleton(new IncomingWorkerOptions { Enabled = true, EmptyDelay = TimeSpan.FromMilliseconds(10), ErrorDelay = TimeSpan.FromMilliseconds(10) });
        services.AddInboundFoundations();
        services.AddSingleton<IncomingReceiveWorker>();
        await using var provider = services.BuildServiceProvider();
        var channel = provider.GetRequiredService<InboundProcessingChannel>();
        var worker = provider.GetRequiredService<IncomingReceiveWorker>();
        await worker.StartAsync(default);
        try
        {
            await failure.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(channel.TryRead(out _));
            Assert.Equal(3, receiver.Calls); // Failed receipt blocks the next receive.
            failure.Release.TrySetResult();
            await EventuallyAsync(() => Task.FromResult(receiver.Calls == 4));
            Assert.True(channel.TryRead(out var id));
            var stored = (await new InboundReceiptRepository(db).ReadAsync(id, default))!;
            Assert.Equal("   ", stored.Receipt.RawXml);
            Assert.Equal("unsupported", stored.Receipt.MessageType);
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM InboundMessageJournal").SingleAsync());
        }
        finally { failure.Release.TrySetResult(); await worker.StopAsync(default); }
    }

    private sealed class ScriptedReceiver : IIncomingReceiveClient
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public async Task<IncomingReceiveResponse> ReceiveAsync(CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref calls);
            if (call > 3)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return new("BAGAGE22", DateTimeOffset.UtcNow, new(call == 1 ? 503 : 200, call == 2 ? "" : "   ", []), null, null, 1, false);
        }
    }

    private sealed class FailedReceiptSave : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        private int calls;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
                throw new InvalidOperationException("Injected receipt commit failure.");
            }
            return result;
        }
    }

    [Fact]
    public async Task Explicitly_enabled_api_host_runs_incoming_flow_and_keeps_liveness_available()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var receives = 0;
        var submissions = 0;
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            if (context.Request.Method == "GET" && context.Request.Path == "/Message")
            {
                if (Interlocked.Increment(ref receives) != 1)
                {
                    return;
                }

                context.Response.Headers["X-MONTRAN-IPS-MessageSeq"] = "1";
                context.Response.Headers["X-MONTRAN-IPS-MessageType"] = "pacs.008";
                await context.Response.WriteAsync(fixture.Input.Signed["valid"]);
            }
            else if (context.Request.Path == "/api/ips/pacs008/receive")
            {
                Interlocked.Increment(ref submissions);
                await context.Response.WriteAsync("{\"status\":\"ACCP\",\"endToEndId\":\"E2E-1\"}");
            }
            else if (context.Request.Method == "POST" && context.Request.Path == "/Message")
            {
                context.Response.Headers["X-MONTRAN-IPS-ReqSts"] = "ACCP";
                await context.Response.WriteAsync(fixture.Response("accepted").Body);
            }
            else
            {
                context.Response.StatusCode = 404;
            }
        });
        using var certificates = new TransportCertificates();
        var signing = certificates.SavePfx(fixture.Input.Certificate);
        var trust = certificates.SavePublic(fixture.Input.Certificate, "ips.pem");
        await using var db = database.Context();
        using var factory = new LiveApi(new()
        {
            ["ConnectionStrings:Middleware"] = db.Database.GetConnectionString(),
            ["Payments:Incoming:Workers:Enabled"] = "true",
            ["Payments:Incoming:Transport:Enabled"] = "true",
            ["Payments:Incoming:Transport:ParticipantBic"] = "BAGAGE22",
            ["Payments:Incoming:Transport:Ips:BaseUrl"] = server.Url,
            ["Payments:Incoming:Transport:Cbs:BaseUrl"] = server.Url,
            ["Payments:Incoming:Transport:SigningCertificate:Path"] = signing.Path,
            ["Payments:Incoming:Transport:SigningCertificate:Password"] = signing.Password,
            ["Payments:Incoming:Transport:IpsSignatureTrust:0:Path"] = trust.Path,
            ["Payments:Incoming:Protocol:IpsBic"] = "NBGEGE22"
        });
        using var client = factory.CreateClient();
        Assert.Equal("Healthy", await client.GetStringAsync("/health/live"));
        await EventuallyAsync(async () => await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM InboundMessageJournal WHERE Status = 1").SingleAsync() == 1);
        Assert.Equal(1, submissions);
    }

    private sealed class LiveApi(Dictionary<string, string?> settings) : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(new Clock()));
        }
    }

    private IHost Host(SqlTestDatabase database, string url, TransportCertificates certificates, Clock time)
    {
        using var db = database.Context();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddPersistence(db.Database.GetConnectionString()!);
        builder.Services.AddSingleton<TimeProvider>(time);
        builder.Services.AddSingleton(new IncomingTransportSettings
        {
            Enabled = true,
            ParticipantBic = "BAGAGE22",
            Ips = new() { BaseUrl = url, ConnectionLimit = 3 },
            Cbs = new() { BaseUrl = url, ConnectionLimit = 4 },
            SigningCertificate = certificates.SavePfx(fixture.Input.Certificate),
            IpsSignatureTrust = [certificates.SavePublic(fixture.Input.Certificate, "ips.pem")]
        });
        builder.Services.AddSingleton(new IncomingWorkerOptions { Enabled = true, EmptyDelay = TimeSpan.FromMilliseconds(20), ErrorDelay = TimeSpan.FromMilliseconds(20) });
        builder.Services.AddSingleton(new InboundSchedulingOptions(capacity: 2, discoveryBatch: 2, discoveryInterval: TimeSpan.FromMilliseconds(20)));
        builder.Services.AddSingleton(new Pacs008ProtocolProfile("NBGEGE22"));
        builder.Services.AddSingleton(new Pacs008SigningPolicy(false, false));
        builder.Services.AddSingleton<Pacs008MessageSigner>();
        builder.Services.AddIncomingHttpClients();
        builder.Services.AddIncomingWorkers();
        builder.Services.Configure<HostOptions>(o => o.ServicesStopConcurrently = true);
        return builder.Build();
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!await condition())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), "Durable worker result was not observed.");
            await Task.Delay(20);
        }
    }

    private sealed class Clock : TimeProvider
    {
        private long ticks = DateTimeOffset.Parse("2026-10-04T12:00:01Z").UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
    }
}
