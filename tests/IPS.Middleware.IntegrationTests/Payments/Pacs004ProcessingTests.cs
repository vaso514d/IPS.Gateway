using System.Xml.Linq;
using IPS.Middleware.Application.Payments.Pacs004;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Payments;

// pacs.004 shares the outgoing core: same claims, journal, markers and callbacks, with its own content and correlation.
public sealed class Pacs004ProcessingTests
{
    private static readonly XNamespace Pacs = "urn:iso:std:iso:20022:tech:xsd:pacs.004.001.13";
    private static readonly XNamespace Head = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";

    [Fact]
    public async Task Accepted_return_is_sent_once_in_the_IPS_v1_profile_and_reported()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await core.AcceptPacs004Async();

        var outcome = (await core.ProcessAsync(id))!;

        Assert.Equal(TransactionStatus.Accepted, outcome.Status);
        Assert.Equal(StatusSource.Ips, outcome.Source);
        var sent = Assert.Single(core.Ips.Received);
        Pacs008Schema.ValidatePacs004(sent);
        var document = XDocument.Parse(sent);
        Assert.Equal("pacs.004.001.13", document.Descendants(Head + "MsgDefIdr").Single().Value);
        Assert.Equal("RTR-processing", document.Descendants(Head + "BizMsgIdr").Single().Value);
        Assert.Equal("RTR-processing", document.Descendants(Pacs + "MsgId").Single().Value);
        Assert.Equal("RTR-processing", document.Descendants(Pacs + "RtrId").Single().Value);
        Assert.Equal("ORIG-E2E-1", document.Descendants(Pacs + "OrgnlEndToEndId").Single().Value);
        Assert.Equal("ORIG-TX-1", document.Descendants(Pacs + "OrgnlTxId").Single().Value);
        Assert.Equal("100.5", document.Descendants(Pacs + "TtlRtrdIntrBkSttlmAmt").Single().Value);
        Assert.Equal("100.5", document.Descendants(Pacs + "OrgnlIntrBkSttlmAmt").Single().Value);
        Assert.Equal("100.5", document.Descendants(Pacs + "RtrdIntrBkSttlmAmt").Single().Value);
        Assert.Equal("2026-10-04", document.Descendants(Pacs + "GrpHdr").Single().Element(Pacs + "IntrBkSttlmDt")!.Value);
        Assert.Equal("2026-10-03", document.Descendants(Pacs + "OrgnlTxRef").Single().Element(Pacs + "IntrBkSttlmDt")!.Value);
        Assert.Equal("SLEV", document.Descendants(Pacs + "ChrgBr").Single().Value);
        Assert.Equal("FOCR", document.Descendants(Pacs + "Rsn").Single().Element(Pacs + "Cd")!.Value);
        Assert.Equal("BAGAGE22", document.Descendants(Pacs + "AnyBIC").Single().Value);
        var reference = document.Descendants(Pacs + "OrgnlTxRef").Single();
        Assert.Equal("TBCBGE22", reference.Element(Pacs + "DbtrAgt")!.Descendants(Pacs + "BICFI").Single().Value);
        Assert.Equal("BAGAGE22", reference.Element(Pacs + "CdtrAgt")!.Descendants(Pacs + "BICFI").Single().Value);
        Assert.Equal("Original Payer", reference.Element(Pacs + "Dbtr")!.Descendants(Pacs + "Nm").Single().Value);
        Assert.Equal("GE95TB0000000123456789", reference.Element(Pacs + "DbtrAcct")!.Descendants(Pacs + "IBAN").Single().Value);
        Assert.Equal("GE29NB0000000101904917", reference.Element(Pacs + "CdtrAcct")!.Descendants(Pacs + "IBAN").Single().Value);

        // Fields outside the v1 profile are never sent.
        var names = document.Descendants().Select(element => element.Name.LocalName).ToArray();
        Assert.DoesNotContain("OrgnlGrpInf", names);
        Assert.DoesNotContain("OrgnlInstrId", names);
        Assert.DoesNotContain("OrgnlUETR", names);
        Assert.DoesNotContain("InstdAgt", names);
        Assert.DoesNotContain("AddtlInf", names);
        Assert.DoesNotContain("ClrSysMmbId", names);

        var stored = await core.ReadAsync(id);
        Assert.Equal("RTR-processing", stored.Message.MessageId);
        Assert.Equal("RTR-processing", stored.Message.TransactionId);
        Assert.Equal("pacs.004.001.13", (await Journal(core, id)).MessageDefinition);
        Assert.Equal(1, await Callbacks(core, id));
    }

    [Fact]
    public async Task Optional_content_is_sent_only_when_given()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var request = Pacs004Fixture.Request("optional");
        var id = (await core.AcceptAsync(request with
        {
            SenderIndirectParticipant = "MEMBER-1",
            Amount = 40m,
            Debtor = request.Debtor! with { Type = 0, Identifier = "123456789" },
            Creditor = request.Creditor! with { Type = 1, Identifier = "01001" },
            Original = request.Original! with { Amount = 100m, OriginalMessageId = "M-1", OriginalMessageNameId = "pacs.008.001.12" }
        })).Intake!.Payment.Id;

        await core.ProcessAsync(id);

        var sent = Assert.Single(core.Ips.Received);
        Pacs008Schema.ValidatePacs004(sent);
        var document = XDocument.Parse(sent);
        var group = document.Descendants(Pacs + "OrgnlGrpInf").Single();
        Assert.Equal(["M-1", "pacs.008.001.12"], group.Elements().Select(element => element.Value));
        Assert.Equal("100", document.Descendants(Pacs + "OrgnlIntrBkSttlmAmt").Single().Value);
        Assert.Equal("40", document.Descendants(Pacs + "RtrdIntrBkSttlmAmt").Single().Value);
        var reference = document.Descendants(Pacs + "OrgnlTxRef").Single();
        var member = reference.Element(Pacs + "CdtrAgt")!.Descendants(Pacs + "ClrSysMmbId").Single();
        Assert.Equal("MEMBER-1", member.Element(Pacs + "MmbId")!.Value);
        Assert.Equal("GE", member.Descendants(Pacs + "Cd").Single().Value);
        Assert.Equal("123456789", reference.Element(Pacs + "Dbtr")!.Descendants(Pacs + "OrgId").Single().Descendants(Pacs + "Id").Single().Value);
        Assert.Equal("01001", reference.Element(Pacs + "Cdtr")!.Descendants(Pacs + "PrvtId").Single().Descendants(Pacs + "Id").Single().Value);
    }

    [Fact]
    public async Task Only_a_reply_about_the_original_payment_settles_the_return()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        core.Ips.Behavior = (reply, _) => core.Ips.RespondAsync(reply with { TransactionId = "RTR-processing", EndToEndId = "OTHER" }, "ACCP");
        var id = await core.AcceptPacs004Async();

        var outcome = (await core.ProcessAsync(id))!;

        // The reply echoes our own ids instead of the original payment's, so it does not belong to this return.
        Assert.Equal(TransactionStatus.Uncertain, outcome.Status);
    }

    [Fact]
    public async Task Rejected_return_is_final_with_the_ips_reason()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        core.Ips.Behavior = (reply, _) => core.Ips.RespondAsync(reply with { GroupStatus = "RJCT", TransactionStatus = "RJCT", ReasonCode = "AC01" }, "RJCT/1009");
        var id = await core.AcceptPacs004Async();

        var outcome = (await core.ProcessAsync(id))!;

        Assert.Equal(TransactionStatus.Rejected, outcome.Status);
        Assert.Equal("AC01", outcome.Details.ReasonCode);
        Assert.Equal(1, await Callbacks(core, id));
    }

    [Fact]
    public async Task Unknown_reply_leaves_the_return_uncertain_without_a_pre_send_deadline()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost", []));
        var id = await core.AcceptPacs004Async();

        core.Clock.Now += TimeSpan.FromHours(2);
        var outcome = (await core.ProcessAsync(id))!;

        Assert.Equal(TransactionStatus.Uncertain, outcome.Status);
        Assert.Single(core.Ips.Received);
        Assert.Empty(core.Ips.Resent);
    }

    [Fact]
    public async Task Intake_reports_validation_errors_and_is_idempotent_by_client_reference()
    {
        await using var core = await ProcessingHarness.CreateAsync();

        var invalid = await core.AcceptAsync(Pacs004Fixture.Request("bad") with { Amount = 0, ReturnReasonCode = "AC03" });
        Assert.Null(invalid.Intake);
        Assert.Contains(invalid.Errors, error => error.Field == "amount");
        Assert.Contains(invalid.Errors, error => error.Field == "returnReasonCode");

        var first = await core.AcceptAsync(Pacs004Fixture.Request("same"));
        Assert.True(first.Intake!.Created);
        var repeat = await core.AcceptAsync(Pacs004Fixture.Request("same") with { Amount = -1 });
        Assert.False(repeat.Intake!.Created);
        Assert.Equal(first.Intake.Payment.Id, repeat.Intake.Payment.Id);
    }

    [Fact]
    public async Task A_return_id_already_used_by_another_payment_is_rejected()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        await core.AcceptAsync(Pacs004Fixture.Request("one") with { Id = "SHARED" });

        var result = await core.AcceptAsync(Pacs004Fixture.Request("two") with { Id = "SHARED" });

        Assert.Null(result.Intake);
        Assert.Contains(result.Errors, error => error.Field == "id");
    }

    [Fact]
    public async Task A_return_id_used_by_a_pacs009_message_id_is_rejected()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        await core.AcceptAsync(Pacs009Fixture.Request("nine") with { Id = "SHARED" });

        var result = await core.AcceptAsync(Pacs004Fixture.Request("four") with { Id = "SHARED" });

        Assert.Null(result.Intake);
        Assert.Contains(result.Errors, error => error.Field == "id");
    }

    [Fact]
    public async Task Concurrent_requests_cannot_both_take_the_same_id()
    {
        await using var core = await ProcessingHarness.CreateAsync();

        var results = await Task.WhenAll(
            core.AcceptAsync(Pacs004Fixture.Request("first") with { Id = "SAME-ID" }),
            core.AcceptAsync(Pacs004Fixture.Request("second") with { Id = "SAME-ID" }));

        Assert.Single(results, result => result.Intake is { Created: true });
        var loser = Assert.Single(results, result => result.Intake is null);
        Assert.Contains(loser.Errors, error => error.Field == "id");
    }

    [Fact]
    public async Task The_sent_message_is_signed_and_verifies_independently()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await core.AcceptPacs004Async();

        await core.ProcessAsync(id);

        var sent = Assert.Single(core.Ips.Received);
        Assert.Contains("Signature", sent);
        Assert.True(SignatureVerifier.Verifies(sent, core.SigningCertificate));
        Assert.Equal(sent, (await core.ReadAsync(id)).Message.SignedXml);
    }

    [Theory]
    [InlineData("before-unsigned")]
    [InlineData("before-ready")]
    [InlineData("before-marker")]
    [InlineData("before-outcome")]
    public async Task A_crash_at_each_checkpoint_resumes_with_the_committed_bytes_and_sends_once(string checkpoint)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await core.AcceptPacs004Async();
        var crash = new CrashOnSave(entry =>
        {
            var messages = entry.Context.ChangeTracker.Entries<OutgoingMessageRow>().ToArray();
            return checkpoint switch
            {
                "before-unsigned" => entry.Context.ChangeTracker.Entries<OutgoingPaymentMetadata>().Any(row => row.Property(r => r.UnsignedXml).IsModified),
                "before-ready" => messages.Any(row => row.State == EntityState.Added && row.Entity.Direction == OutgoingMessageDirection.Outbound),
                "before-marker" => messages.Any(row => row.Entity.Status == MessageJournalStatus.SendStarted && row.Property(r => r.Status).IsModified),
                _ => entry.Entity.IsFinal
            };
        });
        await Assert.ThrowsAsync<SimulatedCrash>(() => core.ProcessAsync(id, default, crash));
        var crashed = await core.ReadAsync(id);

        core.Clock.Now += ProcessingHarness.Ownership;
        await core.RecoverAsync(id);
        var outcome = (await core.ProcessAsync(id))!;

        Assert.Equal(TransactionStatus.Accepted, outcome.Status);
        Assert.Single(core.Ips.Received);
        var resumed = await core.ReadAsync(id);
        Assert.Equal(crashed.Message.UnsignedXml ?? resumed.Message.UnsignedXml, resumed.Message.UnsignedXml);
        Assert.Equal(crashed.Message.SignedXml ?? resumed.Message.SignedXml, resumed.Message.SignedXml);
    }

    [Fact]
    public async Task The_stored_snapshot_round_trips_with_the_return_content()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await core.AcceptPacs004Async();

        var stored = await core.ReadAsync(id);

        Assert.Contains("\"returnId\":\"RTR-processing\"", stored.AcceptedJson);
        var correlation = stored.Message.Accepted!.ReplyCorrelation("m", "t");
        Assert.Equal(("m", "ORIG-TX-1", "ORIG-E2E-1"), (correlation.MessageId, correlation.TransactionId, correlation.EndToEndId));
    }

    private static async Task<OutgoingMessage> Journal(ProcessingHarness core, Guid id)
    {
        await using var session = core.Database.Session();
        return (await session.Submissions.ReadJournalAsync(id, default)).Single(message => message.Direction == OutgoingMessageDirection.Outbound);
    }

    private static async Task<int> Callbacks(ProcessingHarness core, Guid id)
    {
        await using var session = core.Database.Session();
        return await session.Context.Set<OutgoingStatusDeliveryRow>().CountAsync(row => row.PaymentId == id);
    }
}
