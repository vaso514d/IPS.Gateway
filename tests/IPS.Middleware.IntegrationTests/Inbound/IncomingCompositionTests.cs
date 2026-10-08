using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Composition;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Replies;
using IPS.Middleware.Application.Inbound.StatusReports;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Inbound;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.Middleware.Infrastructure.Inbound.StatusReports;
using IPS.Middleware.Infrastructure.Inbound.Transfers;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Repositories.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using IPS.Middleware.Infrastructure.UnitOfWork;
using IPS.Middleware.IntegrationTests.Payments;
using IPS.Middleware.IntegrationTests.Transactions;
using IPS.Middleware.IntegrationTests.Transport;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

public sealed class IncomingCompositionTests(IncomingReplyFixture fixture) : IClassFixture<IncomingReplyFixture>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Final_cbs_outcome_sends_immediately_after_atomic_registration_and_release(bool accepted)
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        h.Core.Response = new(200, accepted ? "{\"status\":\"ACCP\"}" : "{\"status\":\"RJCT\",\"reasonCode\":\"AC01\"}");
        h.Reply.Response = fixture.Response(accepted ? "accepted" : "rejected");
        h.Core.BeforeSubmit = async () =>
        {
            var state = await h.Execution.ReadAsync(id, default);
            Assert.NotNull(state!.PaymentId);
            Assert.Equal(h.Time.Now.AddSeconds(1), state.NextActionAtUtc);
            await using var db = h.Database.Context();
            Assert.False(await db.Database.SqlQueryRaw<bool>("SELECT CAST(CASE WHEN ClaimToken IS NULL THEN 0 ELSE 1 END AS bit) AS Value FROM InboundMessageJournal WHERE Id = {0}", id).SingleAsync());
        };
        Assert.Equal(IncomingCompositionStatus.Terminal, (await h.RunAsync(id)).Status);
        Assert.Single(h.Core.Submissions);
        Assert.Single(h.Reply.Messages);
        Assert.Equal(h.Time.Now, (await h.SnapshotAsync(id)).Attempts.Single().StartedAtUtc);
        Assert.True(await JavaSignatureVerifier.VerifyAsync(h.Reply.Messages.Single(), fixture.Input.Certificate));
        Assert.False(h.Channel.TryRead(out _));
        Assert.Equal(id, await h.SeedAsync());
        await h.RunAsync(id);
        Assert.Single(h.Core.Submissions);
        Assert.Single(h.Reply.Messages);
    }

    [Fact]
    public async Task Trusted_structural_rejection_freezes_reply_without_payment_or_cbs()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync(input: "count");
        h.Reply.Response = fixture.Response("rejected");
        await h.RunAsync(id);
        var reply = await h.SnapshotAsync(id);
        Assert.Equal("FF01", reply.Envelope.Decision.ReasonCode);
        Assert.Null((await h.Execution.ReadAsync(id, default))!.PaymentId);
        Assert.Empty(h.Core.Submissions);
        Assert.Single(h.Reply.Messages);
        Assert.Equal(IncomingReplyStatus.Delivered, reply.Status);
    }

    // 012b: a payment is judged as of its receipt. Received one tick before the IPS certificate became valid, it is held
    // with the validity reason even though it is processed while the certificate is valid, and nothing reaches the CBS or IPS.
    [Fact]
    public async Task A_payment_received_outside_its_certificate_validity_is_held_without_a_payment_or_remote_call()
    {
        var certificate = fixture.Input.Certificate;
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync(receivedAt: SignatureValidity.At(certificate, "tick-before-start"));

        Assert.Equal(IncomingCompositionStatus.Held, (await h.RunAsync(id)).Status);

        await using var db = h.Database.Context();
        var receipt = (await new InboundReceiptRepository(db).ReadAsync(id, default))!;
        Assert.Equal(InboundProcessingStatus.Held, receipt.Status);
        Assert.Equal(SignatureValidity.Stored(SignatureValidity.IncomingHold(certificate)), receipt.HoldReason);
        Assert.False(await db.Set<IncomingPayment>().AnyAsync());
        Assert.Empty(h.Core.Submissions);
        Assert.Empty(h.Reply.Messages);
    }

    // 012b: the reply step reads the payment again after the CBS credited it, by then past the certificate's expiry. The
    // payment was received while the certificate was valid, so the pacs.002 is still built, signed and delivered.
    [Fact]
    public async Task A_payment_received_while_the_certificate_was_valid_is_answered_after_the_certificate_expired()
    {
        var certificate = fixture.Input.Certificate;
        var notAfter = SignatureValidity.At(certificate, "not-after");
        // Our signing certificate and the IPS certificate that signs IPS's answer are the current ones at reply time.
        using var current = IpsReplies.Certificate("CN=Current IPS", notAfter.AddDays(-30), notAfter.AddYears(1));
        var answer = (await IpsReplies.SignAsync(current, IpsReplies.Unsigned(IncomingReplyFixture.Reference)))[0];
        await using var h = await Harness.CreateAsync(fixture, configure: services => services.AddSingleton<IIncomingReplyProtocol>(provider =>
        {
            var time = provider.GetRequiredService<TimeProvider>();
            return new IncomingReplyProtocol(new(new(false, false), time), new Certificates(current), new IpsSignatureTrust([certificate, current], time));
        }));
        // The minimal payment has no acceptance time, so its payment window runs from the receipt.
        h.Time.Now = notAfter.AddSeconds(-1);
        var id = await h.SeedAsync(input: "minimal");
        h.Reply.Response = new(200, answer, [new("X-MONTRAN-IPS-ReqSts", "ACCP")]);
        h.Core.AfterSubmit = () => h.Time.Now = notAfter.AddTicks(1);

        await h.RunAsync(id);

        Assert.Single(h.Core.Submissions);
        var reply = await h.SnapshotAsync(id);
        Assert.Equal(IncomingReplyStatus.Delivered, reply.Status);
        Assert.True(reply.Envelope.Decision.Accepted);
        Assert.True(await JavaSignatureVerifier.VerifyAsync(Assert.Single(h.Reply.Messages), current));
    }

    [Theory]
    [InlineData("untrusted", "pacs.008", 1)]
    [InlineData("wrong-version", "pacs.008", 1)]
    [InlineData("valid", "camt.053", 1)]
    [InlineData("valid", "pacs.008", 0)]
    [InlineData("valid", "camt.056", 0)]
    [InlineData("valid", "camt.055", 0)]
    public async Task Held_receipts_never_call_remote_systems(string input, string type, long sequence)
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync(sequence, input, type);
        Assert.Equal(IncomingCompositionStatus.Held, (await h.RunAsync(id)).Status);
        Assert.Empty(h.Core.Submissions);
        Assert.Empty(h.Reply.Messages);
    }

    [Theory]
    [InlineData("camt.056")]
    [InlineData("camt.056.001.11")]
    [InlineData("camt.029")]
    [InlineData("camt.029.001.13")]
    [InlineData("camt.055")]
    [InlineData("camt.055.001.12")]
    [InlineData("camt.055.001.012")]
    [InlineData("camt.055.001.08")]
    // 012c: recalls and cancellations are no longer archived unread; an unverifiable one is held like any other transfer.
    public async Task An_unverifiable_recall_or_cancellation_is_held_without_a_payment_a_transfer_a_reply_or_any_remote_call(string type)
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync(1, rawXml: "<archived recall />", type: type);

        var result = await h.RunAsync(id);

        Assert.Equal(IncomingCompositionStatus.Held, result.Status);
        var state = (await h.Execution.ReadAsync(id, default))!;
        Assert.Equal(InboundProcessingStatus.Held, state.Status);
        Assert.Null(state.PaymentId);
        Assert.False(state.HasReply);
        Assert.Empty(h.Core.Submissions);
        Assert.Empty(h.Reply.Messages);
        await using var db = h.Database.Context();
        var stored = (await new InboundReceiptRepository(db).ReadAsync(id, default))!;
        Assert.Equal("<archived recall />", stored.Receipt.RawXml);
        Assert.NotNull(stored.HoldReason);
        Assert.False(await db.Set<IncomingTransferMetadata>().AnyAsync());
    }

    [Fact]
    public async Task Competing_receipts_share_one_cbs_submission_and_keep_distinct_reply_references()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var first = await h.SeedAsync();
        var second = await h.SeedAsync(2, "alternative");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Core.BeforeSubmit = async () => { entered.SetResult(); await release.Task; };
        var owner = h.RunAsync(first);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.Equal(IncomingCompositionStatus.Deferred, (await h.RunAsync(second)).Status);
            Assert.Equal(h.Time.Now.AddSeconds(1), (await h.Execution.ReadAsync(second, default))!.NextActionAtUtc);
            Assert.Single(h.Core.Submissions);
        }
        finally { release.SetResult(); }
        await owner;
        h.Reply.Response = fixture.Response("alternative");
        // A committed decision makes the first reply eligible without waiting for the continuation delay.
        await h.RunAsync(second);
        Assert.Single(h.Core.Submissions);
        Assert.Equal(2, h.Reply.Messages.Count);
        Assert.Equal("IN-GROUP-1", (await h.SnapshotAsync(first)).Envelope.Original.GroupMessageId);
        Assert.Equal("IN-GROUP-2", (await h.SnapshotAsync(second)).Envelope.Original.GroupMessageId);
    }

    [Fact]
    public async Task Full_or_lost_reply_notifications_do_not_lose_durable_retries()
    {
        await using var h = await Harness.CreateAsync(fixture, capacity: 1);
        var id = await h.SeedAsync();
        Assert.True(h.Channel.TryNotify(Guid.NewGuid()));
        h.Reply.Response = new(503, "lost", []);
        Assert.Equal(IncomingCompositionStatus.ReplyReady, (await h.RunAsync(id)).Status);
        var stored = await h.SnapshotAsync(id);
        await h.RunAsync(id); // duplicate notification before the stored due time
        Assert.Single(h.Reply.Messages);
        Assert.True(h.Channel.TryRead(out var unrelated));
        Assert.NotEqual(id, unrelated);
        h.Time.Now += TimeSpan.FromMilliseconds(200);
        await h.RunAsync(id); // rediscovery supplies ID independently of channel contents
        Assert.Equal(IncomingReplyStatus.ManualReview, (await h.SnapshotAsync(id)).Status);
        Assert.Equal(new[] { stored.MessageXml, stored.MessageXml }, h.Reply.Messages.ToArray());
        Assert.Single(h.Core.Submissions);
    }

    [Theory]
    [InlineData("registration")]
    [InlineData("decision")]
    [InlineData("reply-response")]
    public async Task Restart_resumes_committed_boundaries_without_repeating_effects(string checkpoint)
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        var registered = await h.Execution.PrepareAsync(id, default);
        Assert.NotNull(registered.PaymentId);
        if (checkpoint != "registration")
        {
            await h.Execution.ProcessPaymentAsync(registered.PaymentId.Value, default);
        }

        if (checkpoint == "reply-response")
        {
            using var stop = new CancellationTokenSource();
            h.Reply.AfterSend = stop.Cancel;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.RunAsync(id, stop.Token));
            Assert.NotNull((await h.SnapshotAsync(id)).Attempts.Single().Completion);
            h.Reply.AfterSend = null;
            Assert.False(h.Channel.TryRead(out _));
        }
        h.Time.Now += TimeSpan.FromSeconds(checkpoint == "reply-response" ? 46 : 1);
        // Every invocation uses newly constructed scopes and SQL evidence, never the previous EF context.
        await h.RunAsync(id);
        Assert.Single(h.Core.Submissions);
        Assert.Single(h.Reply.Messages);
        Assert.Equal(IncomingReplyStatus.Delivered, (await h.SnapshotAsync(id)).Status);
    }

    [Fact]
    public async Task First_reply_readiness_is_fenced_and_cannot_displace_an_owner_or_override_retry()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        var registered = await h.Execution.PrepareAsync(id, default);
        await h.Execution.ProcessPaymentAsync(registered.PaymentId!.Value, default);
        await using var one = h.Database.Context();
        await using var two = h.Database.Context();
        Assert.True(await new IncomingCompositionRepository(one).StageFirstReplyReadyAsync(id, h.Time.Now, default));
        Assert.True(await new IncomingCompositionRepository(two).StageFirstReplyReadyAsync(id, h.Time.Now, default));
        await new UnitOfWork(one).SaveAsync();
        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() => new UnitOfWork(two).SaveAsync());
        await using (var owner = h.Database.Context())
        {
            Assert.NotNull(await new InboundWorkRepository(owner).StageClaimAsync(id, h.Time.Now, TimeSpan.FromSeconds(45), default));
            await new UnitOfWork(owner).SaveAsync();
        }
        Assert.False(await h.Execution.MakeFirstReplyReadyAsync(id, default));
        h.Time.Now += TimeSpan.FromSeconds(46);
        h.Reply.Response = new(503, "", []);
        await h.RunAsync(id);
        var scheduled = (await h.Execution.ReadAsync(id, default))!.NextActionAtUtc;
        Assert.False(await h.Execution.MakeFirstReplyReadyAsync(id, default));
        Assert.Equal(scheduled, (await h.Execution.ReadAsync(id, default))!.NextActionAtUtc);
    }

    [Fact]
    public async Task Composition_runs_through_real_http_adapters_without_acknowledgements()
    {
        var paths = new ConcurrentQueue<string>();
        var reply = fixture.Response("accepted");
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            paths.Enqueue(context.Request.Path.Value!);
            if (context.Request.Path == "/api/ips/pacs008/receive")
            {
                Assert.Equal("E2E-1", context.Request.Headers["Idempotency-Key"]);
                await context.Response.WriteAsync("{\"status\":\"ACCP\",\"endToEndId\":\"E2E-1\"}");
            }
            else if (context.Request.Path == "/Message")
            {
                Assert.Equal("BAGAGE22", context.Request.Headers["X-MONTRAN-IPS-Channel"]);
                context.Response.Headers["X-MONTRAN-IPS-ReqSts"] = "ACCP";
                await context.Response.WriteAsync(reply.Body);
            }
            else
            {
                context.Response.StatusCode = 404;
            }
        });
        using var certificates = new TransportCertificates();
        await using var h = await Harness.CreateAsync(fixture, configure: services =>
        {
            services.AddSingleton(new IncomingTransportSettings
            {
                Enabled = true,
                ParticipantBic = "BAGAGE22",
                Ips = new() { BaseUrl = server.Url },
                Cbs = new() { BaseUrl = server.Url },
                SigningCertificate = certificates.SavePfx(fixture.Input.Certificate),
                IpsSignatureTrust = [certificates.SavePublic(fixture.Input.Certificate, "ips.pem")]
            });
            services.AddSingleton(new Pacs008SigningPolicy(false, false));
            services.AddSingleton<Pacs008MessageSigner>();
            services.AddIncomingHttpClients();
        });
        Assert.Equal(IncomingCompositionStatus.Terminal, (await h.RunAsync(await h.SeedAsync())).Status);
        Assert.Equal(new[] { "/api/ips/pacs008/receive", "/Message" }, paths.ToArray());
    }

    [Fact]
    public async Task Failed_registration_rolls_back_attachment_and_payment_and_publishes_nothing()
    {
        await using var h = await Harness.CreateAsync(fixture, configure: services =>
            services.AddDbContext<TransactionDbContext>(options => options.AddInterceptors(new FailRegistration())));
        var id = await h.SeedAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.RunAsync(id));
        var state = await h.Execution.ReadAsync(id, default);
        Assert.Null(state!.PaymentId);
        Assert.False(state.HasReply);
        await using var db = h.Database.Context();
        Assert.Equal(0, await db.Set<IncomingPayment>().CountAsync());
        Assert.Empty(h.Core.Submissions);
        Assert.Empty(h.Reply.Messages);
        Assert.False(h.Channel.TryRead(out _));
    }

    [Fact]
    public async Task Abandoned_cbs_marker_queries_status_without_repeating_submission()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        var registered = await h.Execution.PrepareAsync(id, default);
        await using (var db = h.Database.Context())
        {
            var work = new IncomingPaymentWorkRepository(db);
            var claim = (await work.StageClaimAsync(registered.PaymentId!.Value, h.Time.Now, TimeSpan.FromSeconds(1), default))!;
            var unit = new UnitOfWork(db);
            await unit.SaveAsync();
            var processing = new IncomingProcessingRepository(db);
            var snapshot = (await processing.ReadAsync(claim.PaymentId, default))!;
            snapshot.Payment.BeginSubmission(h.Time.Now);
            await processing.StageCallAsync(claim, CoreCallKind.Submission, h.Time.Now, default);
            await unit.SaveAsync();
        }
        h.Time.Now += TimeSpan.FromSeconds(1);
        await h.RunAsync(id);
        Assert.Empty(h.Core.Submissions);
        Assert.Equal(1, h.Core.Queries);
        Assert.Single(h.Reply.Messages);
        Assert.Equal(IncomingReplyStatus.Delivered, (await h.SnapshotAsync(id)).Status);
    }

    [Fact]
    public async Task Conflicting_payment_details_are_held_without_another_submission_or_reply()
    {
        await using var h = await Harness.CreateAsync(fixture);
        await h.RunAsync(await h.SeedAsync());
        var xml = IncomingPacs008Fixture.Xml.Replace("12.50", "13.50", StringComparison.Ordinal);
        var signed = (await IpsReplies.SignAsync(fixture.Input.Certificate, xml))[0];
        var conflict = await h.SeedAsync(2, rawXml: signed);
        Assert.Equal(IncomingCompositionStatus.Held, (await h.RunAsync(conflict)).Status);
        Assert.Single(h.Core.Submissions);
        Assert.Single(h.Reply.Messages);
        Assert.Null((await h.Execution.ReadAsync(conflict, default))!.PaymentId);
    }

    [Fact]
    public async Task A_live_receipt_owner_prevents_competing_processing()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        await using (var db = h.Database.Context())
        {
            Assert.NotNull(await new InboundWorkRepository(db).StageClaimAsync(id, h.Time.Now, TimeSpan.FromSeconds(45), default));
            await new UnitOfWork(db).SaveAsync();
        }
        Assert.Equal(IncomingCompositionStatus.OwnershipLost, (await h.RunAsync(id)).Status);
        Assert.Empty(h.Core.Submissions);
        Assert.Empty(h.Reply.Messages);
    }

    [Fact]
    public async Task Saved_cbs_response_survives_cancellation_and_is_replayed_before_deadline_decisions()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        using var stop = new CancellationTokenSource();
        h.Core.AfterSubmit = stop.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.RunAsync(id, stop.Token));
        Assert.False(h.Channel.TryRead(out _));
        Assert.Empty(h.Reply.Messages);
        h.Core.AfterSubmit = null;
        h.Time.Now += TimeSpan.FromSeconds(46);
        h.Reply.Response = fixture.Response("rejected");
        await h.RunAsync(id);
        Assert.Single(h.Core.Submissions);
        Assert.Equal(0, h.Core.Queries);
        Assert.Equal(IncomingReplyStatus.Delivered, (await h.SnapshotAsync(id)).Status);
        await using var db = h.Database.Context();
        var payment = await db.Set<IncomingPayment>().SingleAsync();
        Assert.Equal(CoreOutcome.Accepted, payment.CoreStatus);
        Assert.False(payment.IpsDecision!.Accepted);
    }

    private sealed class FailRegistration : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<IncomingPayment>().Any(e => e.State == EntityState.Added))
            {
                throw new InvalidOperationException("Injected registration commit failure.");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-04T12:00:01Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Certificates(X509Certificate2 certificate) : ISigningCertificateSource
    {
        public ValueTask<X509Certificate2?> GetCurrentAsync(CancellationToken token) => ValueTask.FromResult<X509Certificate2?>(certificate);
    }
    private sealed class Core : IIncomingCoreClient
    {
        public ConcurrentQueue<string> Submissions { get; } = new();
        public int Queries { get; private set; }
        public CoreResponse Response { get; set; } = new(200, "{\"status\":\"ACCP\"}");
        public Func<Task>? BeforeSubmit { get; set; }
        public Action? AfterSubmit { get; set; }
        public async Task<CoreResponse> SubmitAsync(string participant, Pacs008Request payment, CancellationToken token)
        {
            Submissions.Enqueue(payment.EndToEndId!);
            if (BeforeSubmit is not null)
            {
                await BeforeSubmit();
            }

            AfterSubmit?.Invoke();
            return Response;
        }
        public Task<CoreResponse> QueryAsync(string participant, string endToEndId, CancellationToken token)
        {
            Queries++;
            return Task.FromResult(Response);
        }
    }
    private sealed class Reply : IIncomingReplyClient
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public required IpsSubmissionResponse Response { get; set; }
        public Action? AfterSend { get; set; }
        public Task<IpsSubmissionResponse> SendAsync(string participant, string xml, CancellationToken token)
        {
            Messages.Enqueue(xml);
            AfterSend?.Invoke();
            return Task.FromResult(Response);
        }
    }
    private sealed class Harness : IAsyncDisposable
    {
        public required SqlTestDatabase Database { get; init; }
        public required ServiceProvider Services { get; set; }
        public required IncomingReplyFixture Fixture { get; init; }
        public Clock Time { get; } = new();
        public Core Core { get; } = new();
        public required Reply Reply { get; init; }
        public IIncomingWorkflowExecution Execution => Services.GetRequiredService<IIncomingWorkflowExecution>();
        public InboundReplyChannel Channel => Services.GetRequiredService<InboundReplyChannel>();
        public Task<IncomingCompositionResult> RunAsync(Guid id, CancellationToken token = default) => Services.GetRequiredService<IncomingComposition>().ProcessAsync(id, token);
        public static async Task<Harness> CreateAsync(IncomingReplyFixture fixture, int capacity = 256, Action<IServiceCollection>? configure = null)
        {
            var database = await SqlTestDatabase.CreateAsync();
            var services = new ServiceCollection();
            await using var db = database.Context();
            services.AddPersistence(db.Database.GetConnectionString()!);
            var h = new Harness { Database = database, Fixture = fixture, Services = null!, Reply = new() { Response = fixture.Response("accepted") } };
            services.AddSingleton<TimeProvider>(h.Time);
            services.AddSingleton<IIncomingCoreClient>(h.Core);
            services.AddSingleton<IIncomingReplyClient>(h.Reply);
            services.AddSingleton(new InboundSchedulingOptions(capacity: capacity, discoveryBatch: capacity));
            services.AddSingleton(new Pacs008ProtocolProfile("NBGEGE22"));
            services.AddSingleton<IIncomingReplyProtocol>(new IncomingReplyProtocol(new(new(false, false), h.Time),
                new Certificates(fixture.Input.Certificate), new IpsSignatureTrust([fixture.Input.Certificate], h.Time)));
            services.AddSingleton<IStatusReportProtocol>(new StatusReportProtocol(new IpsSignatureTrust([fixture.Input.Certificate], h.Time)));
            services.AddSingleton<IIncomingTransferProtocol>(new IncomingPacs009Protocol(new IpsSignatureTrust([fixture.Input.Certificate], h.Time)));
            services.AddSingleton<IIncomingTransferProtocol>(new IncomingPacs004Protocol(new IpsSignatureTrust([fixture.Input.Certificate], h.Time)));
            services.AddSingleton<IIncomingTransferProtocol>(new IncomingCamt056Protocol(new IpsSignatureTrust([fixture.Input.Certificate], h.Time)));
            services.AddSingleton<IIncomingTransferProtocol>(new IncomingCamt055Protocol(new IpsSignatureTrust([fixture.Input.Certificate], h.Time)));
            services.AddSingleton<IIncomingTransferProtocol>(new IncomingCamt029Protocol(new IpsSignatureTrust([fixture.Input.Certificate], h.Time)));
            services.AddIncomingComposition();
            configure?.Invoke(services);
            h.Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            return h;
        }
        public async Task<Guid> SeedAsync(
            long sequence = 1,
            string input = "valid",
            string type = "pacs.008",
            string? rawXml = null,
            DateTimeOffset? receivedAt = null)
        {
            await using var db = Database.Context();
            var receipt = await new InboundReceiptRepository(db).StageRegistrationAsync(new("BAGAGE22", sequence, type,
                rawXml ?? (input == "untrusted" ? IncomingPacs008Fixture.Xml : Fixture.Input.Signed[input]), false, receivedAt ?? Time.Now), default);
            await new UnitOfWork(db).SaveAsync();
            return receipt.JournalId;
        }
        public async Task<IncomingReplySnapshot> SnapshotAsync(Guid id)
        {
            await using var db = Database.Context();
            return (await new IncomingReplyRepository(db).ReadAsync(id, default))!;
        }
        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await Database.DisposeAsync();
        }
    }
}
