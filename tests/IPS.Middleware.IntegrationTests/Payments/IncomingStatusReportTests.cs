using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Composition;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.StatusReports;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Inbound;
using IPS.Middleware.Infrastructure.Inbound.StatusReports;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Inbound.Workers;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Repositories.Inbound;
using IPS.Middleware.Infrastructure.Repositories.Payments;
using IPS.Middleware.IntegrationTests.Transactions;
using IPS.Middleware.IntegrationTests.Transport;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Payments;

// An unsolicited pacs.002 settles the payment it names, through real SQL and independently signed IPS fixtures.
public sealed class IncomingStatusReportTests
{
    [Theory]
    [InlineData(TransactionStatus.Uncertain, "ACCP", TransactionStatus.Accepted)]
    [InlineData(TransactionStatus.Uncertain, "RJCT", TransactionStatus.Rejected)]
    [InlineData(TransactionStatus.Investigating, "ACTC", TransactionStatus.Accepted)]
    [InlineData(TransactionStatus.Resending, "ACSC", TransactionStatus.Accepted)]
    [InlineData(TransactionStatus.Sending, "RJCT", TransactionStatus.Rejected)]
    public async Task Verified_report_settles_a_payment_that_awaits_its_outcome(TransactionStatus state, string status, TransactionStatus expected)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await PaymentAsync(core, state);
        if (state == TransactionStatus.Sending)
        {
            core.Clock.Now += ProcessingHarness.Ownership + TimeSpan.FromSeconds(1);
        }

        var (result, journalId) = await ApplyAsync(core, await ReportAsync(core, id, status));

        Assert.Equal(IncomingCompositionStatus.Terminal, result.Status);
        var stored = await core.ReadAsync(id);
        Assert.Equal(expected, stored.Payment.CurrentStatus);
        Assert.Equal(StatusSource.Ips, stored.Payment.CurrentSource);
        Assert.Null(stored.ClaimToken);
        Assert.Null(stored.NextActionAtUtc);
        Assert.Contains(expected == TransactionStatus.Accepted ? "payment.accepted" : "payment.rejected", stored.Events);
        Assert.Equal(1, await CallbacksAsync(core, id));
        Assert.Equal(InboundProcessingStatus.Processed, (await ReadReceiptAsync(core, journalId)).Status);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("untrusted-signature")]
    [InlineData("unknown-payment")]
    [InlineData("wrong-transaction")]
    [InlineData("wrong-end-to-end")]
    [InlineData("wrong-definition")]
    [InlineData("schema-invalid")]
    [InlineData("malformed")]
    [InlineData("dtd")]
    public async Task Unverifiable_or_unmatched_report_holds_the_receipt_and_changes_nothing(string defect)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await PaymentAsync(core, TransactionStatus.Uncertain);
        using var other = IpsReplies.Certificate("CN=Not IPS");
        var xml = defect switch
        {
            "pending" => await ReportAsync(core, id, "PDNG"),
            "untrusted-signature" => await ReportAsync(core, id, "ACCP", signer: other),
            "unknown-payment" => await ReportAsync(core, id, "ACCP", edit: reply => reply with { MessageId = "unknown-message" }),
            "wrong-transaction" => await ReportAsync(core, id, "ACCP", edit: reply => reply with { TransactionId = "other-transaction" }),
            "wrong-end-to-end" => await ReportAsync(core, id, "ACCP", edit: reply => reply with { EndToEndId = "OTHER-E2E" }),
            "wrong-definition" => await ReportAsync(core, id, "ACCP", edit: reply => reply with { OriginalMessageName = "pacs.009.001.10" }),
            // Signed and naming the payment, but missing a required element; the schema message is long.
            "schema-invalid" => await ReportAsync(core, id, "ACCP", unsignedEdit: unsigned =>
                unsigned.Replace("<pacs:CreDtTm>2026-10-04T14:00:01.000Z</pacs:CreDtTm></pacs:GrpHdr>", "</pacs:GrpHdr>")),
            "dtd" => "<!DOCTYPE Message [<!ENTITY a \"b\">]><Message>&a;</Message>",
            _ => "<bad"
        };
        var before = await core.ReadAsync(id);

        var (result, journalId) = await ApplyAsync(core, xml);

        Assert.Equal(IncomingCompositionStatus.Held, result.Status);
        var receipt = await ReadReceiptAsync(core, journalId);
        Assert.Equal(InboundProcessingStatus.Held, receipt.Status);
        Assert.False(string.IsNullOrWhiteSpace(receipt.HoldReason));
        Assert.True(receipt.HoldReason!.Length <= 100, "A protocol-derived reason is cut to the stored limit.");
        var after = await core.ReadAsync(id);
        Assert.Equal(TransactionStatus.Uncertain, after.Payment.CurrentStatus);
        Assert.Equal(before.NextActionAtUtc, after.NextActionAtUtc);
        Assert.Equal(before.Events, after.Events);
        Assert.Equal(0, await CallbacksAsync(core, id));
    }

    [Theory]
    [InlineData(TransactionStatus.Accepted, "RJCT", "payment.outcome-conflict-observed")]
    [InlineData(TransactionStatus.Accepted, "ACCP", "payment.outcome-observed")]
    [InlineData(TransactionStatus.ManualReview, "ACCP", "payment.outcome-conflict-observed")]
    [InlineData(TransactionStatus.Received, "ACCP", "payment.outcome-conflict-observed")]
    public async Task Payment_not_awaiting_an_outcome_only_observes_the_report(TransactionStatus state, string status, string observation)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await PaymentAsync(core, state);
        var before = await core.ReadAsync(id);
        var callbacks = await CallbacksAsync(core, id);

        var (result, _) = await ApplyAsync(core, await ReportAsync(core, id, status));

        Assert.Equal(IncomingCompositionStatus.Terminal, result.Status);
        var after = await core.ReadAsync(id);
        Assert.Equal(state, after.Payment.CurrentStatus);
        Assert.Equal(before.Payment.CurrentSource, after.Payment.CurrentSource);
        Assert.Equal(before.ClaimToken, after.ClaimToken);
        Assert.Equal(before.NextActionAtUtc, after.NextActionAtUtc);
        Assert.Equal(before.Events.Append(observation), after.Events);
        Assert.Equal(callbacks, await CallbacksAsync(core, id));
    }

    [Fact]
    public async Task A_report_about_a_pacs004_names_the_original_payment_and_settles_the_return()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost", []));
        var id = await core.AcceptPacs004Async();
        await core.ProcessAsync(id);
        Assert.Equal(TransactionStatus.Uncertain, (await core.ReadAsync(id)).Payment.CurrentStatus);
        var report = await ReportAsync(core, id, "ACCP", edit: reply => reply with { OriginalMessageName = "pacs.004.001.13", TransactionId = "ORIG-TX-1" });
        var ownIds = await ReportAsync(core, id, "ACCP", edit: reply => reply with { OriginalMessageName = "pacs.004.001.13" });

        var (held, _) = await ApplyAsync(core, ownIds, sequence: 1);
        Assert.Equal(IncomingCompositionStatus.Held, held.Status);
        var (settled, _) = await ApplyAsync(core, report, sequence: 2);

        Assert.Equal(IncomingCompositionStatus.Terminal, settled.Status);
        var stored = await core.ReadAsync(id);
        Assert.Equal(TransactionStatus.Accepted, stored.Payment.CurrentStatus);
        Assert.Equal(StatusSource.Ips, stored.Payment.CurrentSource);
        Assert.Equal(1, await CallbacksAsync(core, id));
    }

    [Fact]
    public async Task A_report_about_a_camt056_names_the_recalled_payment_and_settles_the_recall()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost", []));
        var id = await core.AcceptCamt056Async();
        await core.ProcessAsync(id);
        Assert.Equal(TransactionStatus.Uncertain, (await core.ReadAsync(id)).Payment.CurrentStatus);
        var report = await ReportAsync(core, id, "ACCP", edit: reply => reply with { OriginalMessageName = "camt.056.001.11", TransactionId = "ORIG-TX-1" });
        var ownIds = await ReportAsync(core, id, "ACCP", edit: reply => reply with { OriginalMessageName = "camt.056.001.11" });

        var (held, _) = await ApplyAsync(core, ownIds, sequence: 1);
        Assert.Equal(IncomingCompositionStatus.Held, held.Status);
        var (settled, _) = await ApplyAsync(core, report, sequence: 2);

        Assert.Equal(IncomingCompositionStatus.Terminal, settled.Status);
        var stored = await core.ReadAsync(id);
        Assert.Equal(TransactionStatus.Accepted, stored.Payment.CurrentStatus);
        Assert.Equal(StatusSource.Ips, stored.Payment.CurrentSource);
        Assert.Equal(1, await CallbacksAsync(core, id));
    }

    [Fact]
    public async Task A_report_about_a_pacs009_settles_it_by_its_own_definition()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost", []));
        var id = await core.AcceptPacs009Async();
        await core.ProcessAsync(id);
        Assert.Equal(TransactionStatus.Uncertain, (await core.ReadAsync(id)).Payment.CurrentStatus);
        var report = await ReportAsync(core, id, "ACCP", edit: reply => reply with { OriginalMessageName = "pacs.009.001.11" });
        var wrongDefinition = await ReportAsync(core, id, "ACCP");

        var (held, _) = await ApplyAsync(core, wrongDefinition, sequence: 1);
        Assert.Equal(IncomingCompositionStatus.Held, held.Status);
        var (settled, _) = await ApplyAsync(core, report, sequence: 2);

        Assert.Equal(IncomingCompositionStatus.Terminal, settled.Status);
        var stored = await core.ReadAsync(id);
        Assert.Equal(TransactionStatus.Accepted, stored.Payment.CurrentStatus);
        Assert.Equal(StatusSource.Ips, stored.Payment.CurrentSource);
        Assert.Equal(1, await CallbacksAsync(core, id));
    }

    [Fact]
    public async Task Observation_keeps_a_waiting_payments_schedule()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await PaymentAsync(core, TransactionStatus.Received);
        var retryAt = core.Clock.Now + TimeSpan.FromMinutes(5);
        await using (var session = core.Database.Session())
        {
            await session.Context.Database.ExecuteSqlRawAsync("UPDATE Transactions SET NextActionAtUtc = {0} WHERE Id = {1}", retryAt, id);
        }

        var (result, _) = await ApplyAsync(core, await ReportAsync(core, id, "ACCP"));

        Assert.Equal(IncomingCompositionStatus.Terminal, result.Status);
        var stored = await core.ReadAsync(id);
        Assert.Equal(TransactionStatus.Received, stored.Payment.CurrentStatus);
        Assert.Equal(retryAt, stored.NextActionAtUtc);
    }

    [Fact]
    public async Task Group_level_rejection_without_a_transaction_element_settles_like_a_direct_reply()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await PaymentAsync(core, TransactionStatus.Uncertain);
        var xml = await ReportAsync(core, id, "RJCT", edit: reply => reply with { IncludeTransaction = false, ReasonCode = "AG09" });

        var (result, _) = await ApplyAsync(core, xml);

        Assert.Equal(IncomingCompositionStatus.Terminal, result.Status);
        var stored = await core.ReadAsync(id);
        Assert.Equal(TransactionStatus.Rejected, stored.Payment.CurrentStatus);
        Assert.Equal("AG09", stored.Payment.CurrentReasonCode);
    }

    [Fact]
    public async Task Live_claim_defers_the_receipt_until_its_owner_is_done()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await PaymentAsync(core, TransactionStatus.Sending);
        var xml = await ReportAsync(core, id, "ACCP");
        var journalId = await RegisterAsync(core, xml);

        var deferred = await RunAsync(core, journalId);

        Assert.Equal(IncomingCompositionStatus.Deferred, deferred.Status);
        var receipt = await ReadReceiptAsync(core, journalId);
        Assert.Equal(InboundProcessingStatus.Pending, receipt.Status);
        Assert.Equal(core.Clock.Now + new IncomingCompositionOptions().ContinuationDelay, receipt.NextActionAtUtc);
        Assert.Equal(TransactionStatus.Sending, (await core.ReadAsync(id)).Payment.CurrentStatus);

        core.Clock.Now += ProcessingHarness.Ownership + TimeSpan.FromSeconds(1);
        var settled = await RunAsync(core, journalId);

        Assert.Equal(IncomingCompositionStatus.Terminal, settled.Status);
        Assert.Equal(TransactionStatus.Accepted, (await core.ReadAsync(id)).Payment.CurrentStatus);
    }

    [Fact]
    public async Task Concurrent_change_to_the_payment_fences_the_report_and_leaves_the_receipt_pending()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await PaymentAsync(core, TransactionStatus.Uncertain);
        var journalId = await RegisterAsync(core, await ReportAsync(core, id, "ACCP"));

        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() => RunAsync(core, journalId, new CompetingWriteOnSave(core.Database, id)));

        Assert.Equal(TransactionStatus.Uncertain, (await core.ReadAsync(id)).Payment.CurrentStatus);
        Assert.Equal(0, await CallbacksAsync(core, id));
        Assert.Equal(InboundProcessingStatus.Pending, (await ReadReceiptAsync(core, journalId)).Status);
    }

    [Fact]
    public async Task Failed_commit_rolls_back_payment_callback_and_receipt_together_then_resumes()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await PaymentAsync(core, TransactionStatus.Uncertain);
        var journalId = await RegisterAsync(core, await ReportAsync(core, id, "ACCP"));

        await Assert.ThrowsAsync<SimulatedCrash>(() => RunAsync(core, journalId, new CrashOnSave(entry => entry.Entity.IsFinal)));

        Assert.Equal(TransactionStatus.Uncertain, (await core.ReadAsync(id)).Payment.CurrentStatus);
        Assert.Equal(0, await CallbacksAsync(core, id));
        Assert.Equal(InboundProcessingStatus.Pending, (await ReadReceiptAsync(core, journalId)).Status);

        // The crashed owner's receipt claim expires, then the next owner applies the report once.
        core.Clock.Now += TimeSpan.FromSeconds(46);
        Assert.Equal(IncomingCompositionStatus.Terminal, (await RunAsync(core, journalId)).Status);
        var stored = await core.ReadAsync(id);
        Assert.Equal(TransactionStatus.Accepted, stored.Payment.CurrentStatus);
        Assert.Single(stored.Events, name => name == "payment.accepted");
        Assert.Equal(1, await CallbacksAsync(core, id));
    }

    [Fact]
    public async Task Redelivered_sequence_is_applied_once()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await PaymentAsync(core, TransactionStatus.Uncertain);
        var xml = await ReportAsync(core, id, "ACCP");
        var (_, journalId) = await ApplyAsync(core, xml, sequence: 7);

        var duplicate = await RegisterAsync(core, xml, sequence: 7);

        Assert.Equal(journalId, duplicate);
        var receipt = await ReadReceiptAsync(core, journalId);
        Assert.Equal(InboundProcessingStatus.Processed, receipt.Status);
        Assert.Equal(1, receipt.DuplicateCount);
        Assert.Single((await core.ReadAsync(id)).Events, name => name == "payment.accepted");
        Assert.Equal(1, await CallbacksAsync(core, id));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(500)]
    public async Task Worker_pulls_applies_and_acknowledges_the_report_even_when_the_ack_fails(int ackStatus)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await PaymentAsync(core, TransactionStatus.Uncertain);
        var xml = await ReportAsync(core, id, "ACCP");
        // The same report twice, then a report without a usable sequence, which is stored held and never acknowledged.
        var deliveries = new ConcurrentQueue<(string Sequence, string Body)>([("7", xml), ("7", xml), ("0", "unreadable")]);
        var acknowledgements = new ConcurrentQueue<(string Method, string Path, string? Sequence, string? Channel)>();
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            if (context.Request.Method == "GET" && context.Request.Path == "/Message")
            {
                if (deliveries.TryDequeue(out var delivery))
                {
                    context.Response.Headers["X-MONTRAN-IPS-MessageSeq"] = delivery.Sequence;
                    context.Response.Headers["X-MONTRAN-IPS-MessageType"] = "pacs.002";
                    await context.Response.WriteAsync(delivery.Body);
                }
                else
                {
                    context.Response.Headers["X-MONTRAN-IPS-ReqSts"] = "EMPTY";
                }
            }
            else if (context.Request.Method == "POST" && context.Request.Path == "/MessageAck")
            {
                acknowledgements.Enqueue((context.Request.Method, context.Request.Path, context.Request.Headers["X-MONTRAN-IPS-MessageSeq"], context.Request.Headers["X-MONTRAN-IPS-Channel"]));
                context.Response.StatusCode = ackStatus;
            }
            else
            {
                context.Response.StatusCode = 404;
            }
        });
        using var certificates = new TransportCertificates();
        using var host = Host(core, server.Url, certificates);
        await host.StartAsync();
        try
        {
            await EventuallyAsync(async () => (await core.ReadAsync(id)).Payment.CurrentStatus == TransactionStatus.Accepted);
            await EventuallyAsync(() => Task.FromResult(acknowledgements.Count == 2));
            await EventuallyAsync(async () =>
            {
                await using var session = core.Database.Session();
                return await session.Context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM InboundMessageJournal").SingleAsync() == 2;
            });
            await Task.Delay(300);
        }
        finally { await host.StopAsync(); }

        // Both deliveries are acknowledged after their receipt is stored; the redelivery is not applied again.
        Assert.All(acknowledgements, ack => Assert.Equal(("POST", "/MessageAck", "7", "BAGAGE22"), ack));
        var stored = await core.ReadAsync(id);
        Assert.Single(stored.Events, name => name == "payment.accepted");
        Assert.Equal(1, await CallbacksAsync(core, id));
    }

    private static async Task<Guid> PaymentAsync(ProcessingHarness core, TransactionStatus state)
    {
        var id = await core.AcceptAsync();
        switch (state)
        {
            case TransactionStatus.Received:
                return id;
            case TransactionStatus.Sending:
                // The crashed owner leaves Sending with a committed claim, live until its ownership time passes.
                await Assert.ThrowsAsync<SimulatedCrash>(() => core.ProcessAsync(id, default, new CrashOnSave(entry => entry.Entity.IsFinal)));
                return id;
            case TransactionStatus.Accepted:
                await core.ProcessAsync(id);
                return id;
        }

        core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost outcome", []));
        await core.ProcessAsync(id);
        core.Ips.Behavior = null;
        await using var session = core.Database.Session();
        var payment = (await session.Payments.FindAsync(id, default))!;
        switch (state)
        {
            case TransactionStatus.Investigating:
                payment.BeginInvestigation(core.Clock.Now);
                break;
            case TransactionStatus.Resending:
                payment.BeginResending(StatusSource.Investigation, core.Clock.Now);
                break;
            case TransactionStatus.ManualReview:
                payment.RequireManualReview(core.Clock.Now);
                break;
        }

        await session.Unit.SaveAsync();
        return id;
    }

    private static async Task<string> ReportAsync(
        ProcessingHarness core,
        Guid id,
        string status,
        Func<IpsReplies.Reply, IpsReplies.Reply>? edit = null,
        X509Certificate2? signer = null,
        Func<string, string>? unsignedEdit = null)
    {
        var message = (await core.ReadAsync(id)).Message;
        var reply = new IpsReplies.Reply
        {
            MessageId = message.MessageId,
            TransactionId = message.TransactionId,
            EndToEndId = message.Accepted!.EndToEndId,
            GroupStatus = status,
            TransactionStatus = status,
            ReasonCode = status == "RJCT" ? "AC01" : null
        };
        var unsigned = IpsReplies.Unsigned(edit?.Invoke(reply) ?? reply);
        return (await IpsReplies.SignAsync(signer ?? core.IpsCertificate, unsignedEdit?.Invoke(unsigned) ?? unsigned))[0];
    }

    private static async Task<(IncomingCompositionResult Result, Guid JournalId)> ApplyAsync(ProcessingHarness core, string xml, long sequence = 1)
    {
        var journalId = await RegisterAsync(core, xml, sequence);
        return (await RunAsync(core, journalId), journalId);
    }

    private static async Task<Guid> RegisterAsync(ProcessingHarness core, string xml, long sequence = 1)
    {
        await using var session = core.Database.Session();
        var registration = await new InboundReceiptRepository(session.Context)
            .StageRegistrationAsync(new("BAGAGE22", sequence, "pacs.002", xml, false, core.Clock.Now), default);
        await session.Unit.SaveAsync();
        return registration.JournalId;
    }

    private static async Task<IncomingCompositionResult> RunAsync(ProcessingHarness core, Guid journalId, IInterceptor? interceptor = null)
    {
        InboundClaim claim;
        await using (var claiming = core.Database.Session())
        {
            var work = new InboundWork(new InboundWorkRepository(claiming.Context), claiming.Unit, core.Clock);
            claim = (await work.AcquireAsync(journalId, TimeSpan.FromSeconds(45), default))!;
        }

        await using var session = core.Database.Session(interceptor is null ? [] : [interceptor]);
        var receipt = (await new InboundReceiptRepository(session.Context).ReadAsync(journalId, default))!.Receipt;
        var handler = new IncomingStatusReportProcessing(
            session.Payments,
            new PaymentPreparationRepository(session.Context),
            session.Work,
            new InboundWorkRepository(session.Context),
            new StatusReportProtocol([core.IpsCertificate]),
            session.Unit,
            new IncomingCompositionOptions(),
            core.Clock);
        return await handler.ProcessAsync(claim, receipt, default);
    }

    private static async Task<StoredInboundReceipt> ReadReceiptAsync(ProcessingHarness core, Guid journalId)
    {
        await using var session = core.Database.Session();
        return (await new InboundReceiptRepository(session.Context).ReadAsync(journalId, default))!;
    }

    private static async Task<int> CallbacksAsync(ProcessingHarness core, Guid id)
    {
        await using var session = core.Database.Session();
        return await session.Context.Set<OutgoingStatusDeliveryRow>().CountAsync(row => row.PaymentId == id);
    }

    private static IHost Host(ProcessingHarness core, string url, TransportCertificates certificates)
    {
        using var db = core.Database.Context();
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Services.AddPersistence(db.Database.GetConnectionString()!);
        // The host loads real test certificates, whose validity starts at the actual current time.
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(new IncomingTransportSettings
        {
            Enabled = true,
            ParticipantBic = "BAGAGE22",
            Ips = new() { BaseUrl = url, ConnectionLimit = 3 },
            Cbs = new() { BaseUrl = url, ConnectionLimit = 4 },
            SigningCertificate = certificates.Identity,
            IpsSignatureTrust = [certificates.SavePublic(core.IpsCertificate, "ips.pem")]
        });
        builder.Services.AddSingleton(new IncomingWorkerOptions { Enabled = true, EmptyDelay = TimeSpan.FromMilliseconds(20), ErrorDelay = TimeSpan.FromMilliseconds(20) });
        builder.Services.AddSingleton(new InboundSchedulingOptions(capacity: 2, discoveryBatch: 2, discoveryInterval: TimeSpan.FromMilliseconds(20)));
        builder.Services.AddSingleton(new Pacs008ProtocolProfile("NBGEGE22"));
        builder.Services.AddSingleton(new Pacs008SigningPolicy(false, false));
        builder.Services.AddSingleton<Pacs008MessageSigner>();
        builder.Services.AddIncomingHttpClients();
        builder.Services.AddIncomingWorkers();
        builder.Services.Configure<HostOptions>(options => options.ServicesStopConcurrently = true);
        return builder.Build();
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!await condition())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), "The durable result was not observed.");
            await Task.Delay(20);
        }
    }

    // Changes the payment row between this scope's read and its first save, as a competing owner would.
    private sealed class CompetingWriteOnSave(SqlTestDatabase database, Guid paymentId) : SaveChangesInterceptor
    {
        private int _fired;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _fired, 1) == 0)
            {
                await using var other = database.Context();
                await other.Database.ExecuteSqlRawAsync("UPDATE Transactions SET CurrentDescription = 'raced' WHERE Id = {0}", paymentId);
            }

            return result;
        }
    }
}
