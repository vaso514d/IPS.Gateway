using System.Security.Cryptography.X509Certificates;
using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Replies;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Repositories.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using IPS.Middleware.Infrastructure.UnitOfWork;
using IPS.Middleware.IntegrationTests.Payments;
using IPS.Middleware.IntegrationTests.Transactions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

public sealed class IncomingReplyTests(IncomingReplyFixture fixture) : IClassFixture<IncomingReplyFixture>
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Final_reply_and_receipt_complete_together_and_duplicate_does_not_send_again(bool accepted)
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync(accepted: accepted);
        h.Client.Response = fixture.Response(accepted ? "accepted" : "rejected");
        await h.RunAsync(id);
        var saved = await h.ReadAsync(id);
        Assert.Equal(IncomingReplyStatus.Delivered, saved.Status);
        Assert.Equal(InboundProcessingStatus.Processed, (await h.ReceiptAsync(id)).Status);
        Assert.True(Assert.Single(saved.Attempts).Consumed);
        Assert.True(await JavaSignatureVerifier.VerifyAsync(saved.MessageXml!, fixture.Input.Certificate));
        await h.SeedAsync(accepted: accepted);
        await h.RunAsync(id);
        Assert.Single(h.Client.Messages);
        Assert.Equal(1, (await h.ReceiptAsync(id)).DuplicateCount);
    }

    [Fact]
    public async Task Lost_reply_resumes_same_xml_after_deadline_and_keeps_two_attempt_budget()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        h.Client.LoseReply = true;
        await h.RunAsync(id);
        var first = await h.ReadAsync(id);
        Assert.Equal(IncomingReplyStatus.Ready, first.Status);
        Assert.NotNull(first.Attempts[0].Completion!.Failure);
        await h.RunAsync(id); // Not yet due.
        Assert.Single(h.Client.Messages);
        h.Time.Now += TimeSpan.FromHours(1);
        h.Profile = new("TBCBGE22", "SEPA"); // The prepared artifact retains its original profile.
        await h.RunAsync(id);
        Assert.Equal(IncomingReplyStatus.ManualReview, (await h.ReadAsync(id)).Status);
        Assert.Equal(InboundProcessingStatus.Held, (await h.ReceiptAsync(id)).Status);
        Assert.Equal(2, h.Client.Messages.Count);
        Assert.Equal(first.MessageXml, h.Client.Messages[0]);
        Assert.Equal(h.Client.Messages[0], h.Client.Messages[1]);
        h.Options = new(maxAttempts: 10); // A new process/configuration cannot replenish a saved budget.
        await h.RunAsync(id);
        Assert.Equal(2, h.Client.Messages.Count);
    }

    [Fact]
    public async Task Saved_response_is_consumed_after_restart_without_another_send()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        using var stop = new CancellationTokenSource();
        h.Client.Response = fixture.Response("accepted");
        h.Client.AfterSend = () => stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.RunAsync(id, stop.Token));
        var saved = await h.ReadAsync(id);
        Assert.NotNull(Assert.Single(saved.Attempts).Completion);
        Assert.False(saved.Attempts[0].Consumed);
        h.Time.Now += TimeSpan.FromMinutes(1);
        h.Client.AfterSend = null;
        await h.RunAsync(id);
        Assert.Single(h.Client.Messages);
        Assert.Equal(IncomingReplyStatus.Delivered, (await h.ReadAsync(id)).Status);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Marker_only_crash_consumes_an_attempt_and_never_resets_budget(int markedAttempts)
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        await h.PrepareOnlyAsync(id);
        for (var i = 0; i < markedAttempts; i++)
        {
            await using var db = h.Database.Context();
            var claim = await h.ClaimAsync(db, id);
            await new IncomingReplyRepository(db).StageAttemptAsync(claim, h.Time.Now, default);
            await new UnitOfWork(db).SaveAsync();
            h.Time.Now += TimeSpan.FromMinutes(1);
        }

        h.Client.Response = fixture.Response("accepted");
        await h.RunAsync(id);
        Assert.Equal(2, (await h.ReadAsync(id)).Attempts.Count);
        Assert.Equal(markedAttempts == 1 ? IncomingReplyStatus.Delivered : IncomingReplyStatus.ManualReview, (await h.ReadAsync(id)).Status);
        Assert.Equal(2 - markedAttempts, h.Client.Messages.Count);
    }

    [Fact]
    public async Task Certificate_failure_defers_without_sending_and_reuses_context_and_unsigned_xml()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        h.Certificates.Certificate = null;
        await h.RunAsync(id);
        var before = await h.ReadAsync(id);
        Assert.Equal(IncomingReplyStatus.Preparing, before.Status);
        Assert.Empty(before.Attempts);
        Assert.Empty(h.Client.Messages);
        h.Time.Now += TimeSpan.FromSeconds(5);
        h.Certificates.Certificate = fixture.Input.Certificate;
        h.Profile = new("TBCBGE22");
        h.Client.Response = fixture.Response("accepted");
        await h.RunAsync(id);
        var after = await h.ReadAsync(id);
        Assert.Equivalent(before.Envelope.Context, after.Envelope.Context, strict: true);
        Assert.Equal(before.UnsignedXml, after.UnsignedXml);
        Assert.Equal("NBGEGE22", after.Envelope.Profile.IpsBic);
        Assert.Equal(IncomingReplyStatus.Delivered, after.Status);
    }

    [Fact]
    public async Task Trusted_count_violation_replies_FF01_without_creating_payment()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync(input: "count");
        h.Client.Response = fixture.Response("rejected");
        await h.RunAsync(id);
        var stored = await h.ReadAsync(id);
        Assert.False(stored.Envelope.Decision.Accepted);
        Assert.Equal("FF01", stored.Envelope.Decision.ReasonCode);
        Assert.Equal(IncomingReplyStatus.Delivered, stored.Status);
        await using var db = h.Database.Context();
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM IncomingPayments").SingleAsync());
    }

    [Theory]
    [InlineData("untrusted", 1)]
    [InlineData("valid", 0)]
    public async Task Untrusted_and_invalid_sequence_receipts_never_send(string input, long sequence)
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync(sequence, input: input);
        await h.RunAsync(id);
        Assert.Empty(h.Client.Messages);
        Assert.Equal(InboundProcessingStatus.Held, (await h.ReceiptAsync(id)).Status);
    }

    [Fact]
    public async Task Contradictory_final_report_is_manual_review_without_changing_local_decision()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        h.Client.Response = fixture.Response("rejected");
        await h.RunAsync(id);
        var stored = await h.ReadAsync(id);
        Assert.True(stored.Envelope.Decision.Accepted);
        Assert.Equal(IncomingReplyStatus.ManualReview, stored.Status);
        Assert.Single(h.Client.Messages);
    }

    [Fact]
    public async Task Competing_instances_send_only_under_the_committed_claim()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Client.Wait = async () =>
        {
            entered.SetResult();
            await release.Task;
        };
        var first = h.RunAsync(id);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await h.RunAsync(id);
        release.SetResult();
        await first;
        Assert.Single(h.Client.Messages);
    }

    [Fact]
    public async Task Expired_owner_cannot_save_a_response_after_reacquisition()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        await h.PrepareOnlyAsync(id);
        await using var first = h.Database.Context();
        var old = await h.ClaimAsync(first, id);
        var oldRepo = new IncomingReplyRepository(first);
        var attempt = await oldRepo.StageAttemptAsync(old, h.Time.Now, default);
        await new UnitOfWork(first).SaveAsync();
        h.Time.Now += TimeSpan.FromMinutes(1);
        await using var second = h.Database.Context();
        await h.ClaimAsync(second, id);
        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() => oldRepo.StageCompletionAsync(old, attempt.Id,
            new(fixture.Response("accepted"), null, h.Time.Now), h.Time.Now, default));
    }

    [Fact]
    public async Task Concurrent_receipt_update_rolls_back_staged_artifact_and_other_changes()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        await using var first = h.Database.Context();
        var claim = await h.ClaimAsync(first, id);
        await h.StageEnvelopeAsync(first, claim);
        await h.SeedAsync(); // Advances the parent rowversion through duplicate metadata.
        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() => new UnitOfWork(first).SaveAsync());
        await using var read = h.Database.Context();
        Assert.Null(await new IncomingReplyRepository(read).ReadAsync(id, default));
        Assert.Equal(1, (await h.ReceiptAsync(id)).DuplicateCount);
    }

    [Fact]
    public async Task Cancellation_before_commit_does_not_publish_artifacts()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        await using var db = h.Database.Context();
        var claim = await h.ClaimAsync(db, id);
        await h.StageEnvelopeAsync(db, claim);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UnitOfWork(db).SaveAsync(new(true)));
        await using var read = h.Database.Context();
        Assert.Null(await new IncomingReplyRepository(read).ReadAsync(id, default));
        Assert.Empty(h.Client.Messages);
    }

    [Fact]
    public async Task Attempt_markers_require_committed_ownership()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        await h.PrepareOnlyAsync(id);
        await using var db = h.Database.Context();
        var claim = await new InboundWorkRepository(db).StageClaimAsync(id, h.Time.Now, h.Options.Ownership, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new IncomingReplyRepository(db).StageAttemptAsync(claim!, h.Time.Now, default));
    }

    [Theory]
    [InlineData("accepted", "ACCP", ReplyDeliveryOutcome.Delivered)]
    [InlineData("rejected", "RJCT/100", ReplyDeliveryOutcome.Conflict)]
    [InlineData("mismatch", "ACCP", ReplyDeliveryOutcome.Unresolved)]
    [InlineData("wrong-version", "ACCP", ReplyDeliveryOutcome.Unresolved)]
    [InlineData("accepted", "SUCCESS", ReplyDeliveryOutcome.Unresolved)]
    [InlineData("accepted", "", ReplyDeliveryOutcome.Unresolved)]
    [InlineData("accepted", "RJCT/100", ReplyDeliveryOutcome.Unresolved)]
    public void Independent_signed_response_evidence_controls_delivery(string name, string status, ReplyDeliveryOutcome expected)
    {
        var protocol = Protocol();
        var response = fixture.Response(name, status);
        Assert.Equal(expected, protocol.Interpret(new(response, null, DateTimeOffset.UtcNow), Envelope()).Outcome);
    }

    [Theory]
    [MemberData(nameof(SignatureValidity.Moments), MemberType = typeof(SignatureValidity))]
    public void A_reply_response_proves_delivery_only_while_its_signing_certificate_is_valid(string moment, bool valid)
    {
        var certificate = fixture.Input.Certificate;
        var clock = new TestClock(SignatureValidity.At(certificate, moment));
        var protocol = new IncomingReplyProtocol(new(new(false, false), clock), new Certificates(certificate), new IpsSignatureTrust([certificate], clock));

        var result = protocol.Interpret(new(fixture.Response("accepted"), null, clock.Now), Envelope());

        if (valid)
        {
            Assert.Equal(ReplyDeliveryOutcome.Delivered, result.Outcome);
        }
        else
        {
            Assert.Equal(ReplyDeliveryOutcome.Unresolved, result.Outcome);
            Assert.Equal(SignatureValidity.ReplyUnresolved(certificate), result.Description);
        }
    }

    [Fact]
    public void Http_success_unsigned_or_empty_body_never_proves_delivery()
    {
        foreach (var body in new[]
        {
            "",
            "not-xml",
            IpsReplies.Unsigned(IncomingReplyFixture.Reference)
        }

        )
        {
            Assert.Equal(ReplyDeliveryOutcome.Unresolved, Protocol().Interpret(new(new(200, body, [new("X-MONTRAN-IPS-ReqSts", "ACCP")]), null,
                DateTimeOffset.UtcNow), Envelope()).Outcome);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Every_preparation_checkpoint_recovers_without_replacing_identifiers(int checkpoint)
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        var before = await h.PrepareOnlyAsync(id, checkpoint, release: false); // Crash: no release, no notification.
        h.Time.Now += TimeSpan.FromHours(1);
        h.Profile = new("TBCBGE22");
        h.Client.Response = fixture.Response("accepted");
        await h.RunAsync(id);
        var after = await h.ReadAsync(id);
        Assert.Equal(IncomingReplyStatus.Delivered, after.Status);
        Assert.Equivalent(before.Envelope.Context, after.Envelope.Context, strict: true);
        Assert.Equal(before.Envelope.Profile.IpsBic, after.Envelope.Profile.IpsBic);
        if (before.UnsignedXml is not null)
        {
            Assert.Equal(before.UnsignedXml, after.UnsignedXml);
        }

        if (before.MessageXml is not null)
        {
            Assert.Equal(before.MessageXml, after.MessageXml);
        }
    }

    [Fact]
    public async Task Two_receipts_for_one_payment_keep_independent_reply_identity()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var first = await h.SeedAsync(1);
        var second = await h.SeedAsync(2, input: "alternative");
        h.Client.Response = fixture.Response("accepted");
        await h.RunAsync(first);
        h.Client.Response = fixture.Response("alternative");
        await h.RunAsync(second);
        var one = await h.ReadAsync(first);
        var two = await h.ReadAsync(second);
        Assert.NotEqual(one.Envelope.Context.MessageId, two.Envelope.Context.MessageId);
        Assert.NotEqual(one.MessageXml, two.MessageXml);
        Assert.Contains("<pacs:OrgnlMsgId>IN-GROUP-1</pacs:OrgnlMsgId>", one.MessageXml);
        Assert.Contains("<pacs:OrgnlMsgId>IN-GROUP-2</pacs:OrgnlMsgId>", two.MessageXml);
        Assert.Equal(IncomingReplyStatus.Delivered, two.Status);
        Assert.Equal(ReplyDeliveryOutcome.Unresolved, h.Protocol.Interpret(new(fixture.Response("accepted"), null, h.Time.Now), two.Envelope).Outcome);
        Assert.Equal(ReplyDeliveryOutcome.Unresolved, h.Protocol.Interpret(new(fixture.Response("alternative"), null, h.Time.Now), one.Envelope).Outcome);
        await using var db = h.Database.Context();
        Assert.Equal(1, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM IncomingPayments").SingleAsync());
    }

    [Fact]
    public async Task Restored_response_headers_are_immutable()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        h.Client.Response = fixture.Response("accepted");
        await h.RunAsync(id);
        var saved = await h.ReadAsync(id);
        var headers = Assert.Single(saved.Attempts).Completion!.Response!.Headers;
        Assert.Throws<NotSupportedException>(() => ((IList<IpsResponseHeader>)headers).Add(new("Changed", "true")));
    }

    [Fact]
    public async Task Expiry_during_remote_call_does_not_allow_stale_completion()
    {
        await using var h = await Harness.CreateAsync(fixture);
        var id = await h.SeedAsync();
        h.Client.Response = fixture.Response("accepted");
        h.Client.AfterSend = () => h.Time.Now += TimeSpan.FromMinutes(1);
        await h.RunAsync(id);
        Assert.Null(Assert.Single((await h.ReadAsync(id)).Attempts).Completion);
        Assert.Equal(InboundProcessingStatus.Pending, (await h.ReceiptAsync(id)).Status);
        h.Client.AfterSend = null;
        await h.RunAsync(id);
        Assert.Equal(2, h.Client.Messages.Count);
        Assert.Equal(IncomingReplyStatus.Delivered, (await h.ReadAsync(id)).Status);
    }

    private IncomingReplyProtocol Protocol() => new(new(new(false, false), TimeProvider.System), new Certificates(fixture.Input.Certificate),
        new IpsSignatureTrust([fixture.Input.Certificate], TimeProvider.System));
    private IncomingReplyEnvelope Envelope() => new("BAGAGE22", ((IncomingPacs008ReadResult.Ready)Protocol().Read(fixture.Input.Signed["valid"], DateTimeOffset.UtcNow)).Payment.Original,
        new(true, DateTimeOffset.UtcNow), new("REPLY", "STATUS", DateTimeOffset.UtcNow), new("NBGEGE22"), 2);
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Certificates(X509Certificate2? certificate) : ISigningCertificateSource
    {
        public X509Certificate2? Certificate { get; set; } = certificate;

        public ValueTask<X509Certificate2?> GetCurrentAsync(CancellationToken token) => ValueTask.FromResult(Certificate);
    }

    private sealed class Remote : IIncomingReplyClient
    {
        public List<string> Messages { get; } = [];
        public IpsSubmissionResponse Response { get; set; } = new(503, "", []);
        public bool LoseReply { get; set; }
        public Action? AfterSend { get; set; }
        public Func<Task>? Wait { get; set; }

        public async Task<IpsSubmissionResponse> SendAsync(string bic, string xml, CancellationToken token)
        {
            Messages.Add(xml); // The independent remote receives the message before a simulated lost response.
            if (Wait is not null)
            {
                await Wait();
            }

            AfterSend?.Invoke();
            if (LoseReply)
            {
                throw new IOException("Reply lost after remote receipt.");
            }

            return Response;
        }
    }

    private sealed class Harness(SqlTestDatabase database, IncomingReplyFixture fixture) : IAsyncDisposable
    {
        public SqlTestDatabase Database { get; } = database;
        public Clock Time { get; } = new();
        public Remote Client { get; } = new();
        public Certificates Certificates { get; } = new(fixture.Input.Certificate);
        public IncomingReplyOptions Options { get; set; } = new();
        public Pacs008ProtocolProfile Profile { get; set; } = new("NBGEGE22");
        public IncomingReplyProtocol Protocol => new(new(new(false, false), Time), Certificates, new IpsSignatureTrust([fixture.Input.Certificate], Time));

        public static async Task<Harness> CreateAsync(IncomingReplyFixture fixture) => new(await SqlTestDatabase.CreateAsync(), fixture);
        public async Task<Guid> SeedAsync(long sequence = 1, bool accepted = true, string input = "valid")
        {
            await using var db = Database.Context();
            var xml = input == "untrusted" ? IncomingPacs008Fixture.Xml : fixture.Input.Signed[input];
            var registered = await new InboundReceiptRepository(db).StageRegistrationAsync(new("BAGAGE22", sequence, "pacs.008", xml, false, Time.Now), default);
            await new UnitOfWork(db).SaveAsync();
            if (!registered.Created || sequence <= 0 || input is not ("valid" or "alternative"))
            {
                return registered.JournalId;
            }

            var incoming = ((IncomingPacs008ReadResult.Ready)Protocol.Read(xml, Time.Now)).Payment;
            var work = new InboundWorkRepository(db);
            var claim = await ClaimAsync(db, registered.JournalId);
            var payments = new IncomingPaymentRepository(db);
            var existing = await payments.FindAsync("BAGAGE22", incoming.Original.EndToEndId, default);
            var payment = existing?.Payment ?? IncomingPayment.Register(Guid.NewGuid(), "BAGAGE22", incoming.Original.EndToEndId, Time.Now);
            if (existing is null)
            {
                payments.Add(payment, incoming.Payment, new(registered.JournalId, Time.Now, Time.Now.AddSeconds(20), incoming.Original));
                payment.BeginSubmission(Time.Now);
                payment.RecordCoreResult(new(accepted ? CoreOutcome.Accepted : CoreOutcome.Rejected, Time.Now, ReasonCode: accepted ? null : "AC01"), Time.Now);
                payment.DecideIps(true, Time.Now);
            }

            await work.StageOriginalReferencesAsync(claim, incoming.Original, Time.Now, default);
            await work.StageAttachmentAsync(claim, payment.Id, Time.Now, default);
            await work.StageFinishAsync(claim, Time.Now, Time.Now, default);
            await new UnitOfWork(db).SaveAsync();
            return registered.JournalId;
        }

        public async Task<InboundClaim> ClaimAsync(TransactionDbContext db, Guid id)
        {
            var claim = await new InboundWorkRepository(db).StageClaimAsync(id, Time.Now, Options.Ownership, default);
            Assert.NotNull(claim);
            await new UnitOfWork(db).SaveAsync();
            return claim;
        }

        public async Task StageEnvelopeAsync(TransactionDbContext db, InboundClaim claim)
        {
            var incoming = ((IncomingPacs008ReadResult.Ready)Protocol.Read(fixture.Input.Signed["valid"], Time.Now)).Payment;
            var repo = new IncomingReplyRepository(db);
            await repo.StageEnvelopeAsync(claim, new("BAGAGE22", incoming.Original, (await repo.ReadDecisionAsync(claim.JournalId, default))!,
                new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), Time.Now), Profile, Options.MaxAttempts), Time.Now, default);
        }

        // Commits preparation up to the checkpoint: 0 envelope, 1 unsigned XML, 2 signed message.
        public async Task<IncomingReplySnapshot> PrepareOnlyAsync(Guid id, int checkpoint = 2, bool release = true)
        {
            await using var db = Database.Context();
            var claim = await ClaimAsync(db, id);
            await StageEnvelopeAsync(db, claim);
            var unit = new UnitOfWork(db);
            await unit.SaveAsync();
            var repo = new IncomingReplyRepository(db);
            if (checkpoint >= 1)
            {
                var xml = Protocol.Build((await repo.ReadAsync(id, default))!.Envelope);
                await repo.StageUnsignedAsync(claim, xml, Time.Now, default);
                await unit.SaveAsync();
                if (checkpoint == 2)
                {
                    await repo.StageMessageAsync(claim, (SignedMessage)await Protocol.SignAsync(xml, default), Time.Now, default);
                    await unit.SaveAsync();
                }
            }

            if (release)
            {
                await new InboundWorkRepository(db).StageFinishAsync(claim, Time.Now, Time.Now, default);
                await unit.SaveAsync();
            }

            return (await repo.ReadAsync(id, default))!;
        }

        public async Task RunAsync(Guid id, CancellationToken token = default)
        {
            await using var db = Database.Context();
            await new IncomingReplyProcessing(new InboundReceiptRepository(db), new InboundWorkRepository(db), new IncomingReplyRepository(db),
                new UnitOfWork(db), Protocol, Client, Profile, Options, Time).ProcessAsync(id, token);
        }

        public async Task<IncomingReplySnapshot> ReadAsync(Guid id)
        {
            await using var db = Database.Context();
            return (await new IncomingReplyRepository(db).ReadAsync(id, default))!;
        }

        public async Task<StoredInboundReceipt> ReceiptAsync(Guid id)
        {
            await using var db = Database.Context();
            return (await new InboundReceiptRepository(db).ReadAsync(id, default))!;
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
}

public sealed class IncomingReplyFixture : IAsyncLifetime
{
    internal IncomingPacs008Fixture Input { get; } = new();

    private readonly Dictionary<string, string> _responses = [];
    internal static IpsReplies.Reply Reference => new()
    {
        MessageId = "IN-GROUP-1",
        TransactionId = "TX-1",
        EndToEndId = "E2E-1"
    };

    public async Task InitializeAsync()
    {
        await Input.InitializeAsync();
        var alternative = IncomingPacs008Fixture.Xml.Replace("IN-GROUP-1", "IN-GROUP-2", StringComparison.Ordinal)
            .Replace("IN-HEADER-1", "IN-HEADER-2", StringComparison.Ordinal).Replace("TX-1", "TX-2", StringComparison.Ordinal);
        Input.Signed["alternative"] = (await IpsReplies.SignAsync(Input.Certificate, alternative))[0];
        var fixtures = new Dictionary<string, IpsReplies.Reply>
        {
            ["accepted"] = Reference,
            ["alternative"] = Reference with { MessageId = "IN-GROUP-2", TransactionId = "TX-2" },
            ["rejected"] = Reference with { GroupStatus = "RJCT", TransactionStatus = "RJCT", ReasonCode = "AC01" },
            ["mismatch"] = Reference with { EndToEndId = "OTHER" },
            ["wrong-version"] = Reference with { OriginalMessageName = "pacs.008.001.11" }
        };
        var signed = await IpsReplies.SignAsync(Input.Certificate, fixtures.Values.Select(IpsReplies.Unsigned).ToArray());
        foreach (var pair in fixtures.Keys.Zip(signed))
        {
            _responses.Add(pair.First, pair.Second);
        }
    }

    internal IpsSubmissionResponse Response(string name, string? status = null) => new(200, _responses[name],
        [new("X-MONTRAN-IPS-ReqSts", status ?? (name == "rejected" ? "RJCT/100" : "ACCP"))]);
    public Task DisposeAsync() => Input.DisposeAsync();
}
