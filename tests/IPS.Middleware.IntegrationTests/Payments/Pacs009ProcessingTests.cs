using System.Xml.Linq;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pacs009;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Payments;

// pacs.009 shares the outgoing core with pacs.008: same claims, journal, markers and callbacks, with its own content.
public sealed class Pacs009ProcessingTests
{
    private static readonly XNamespace Pacs = "urn:iso:std:iso:20022:tech:xsd:pacs.009.001.11";
    private static readonly XNamespace Head = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";

    [Fact]
    public async Task Accepted_payment_is_sent_once_in_the_IPS_v1_profile_and_reported()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await core.AcceptPacs009Async();

        var outcome = (await core.ProcessAsync(id))!;

        Assert.Equal(TransactionStatus.Accepted, outcome.Status);
        Assert.Equal(StatusSource.Ips, outcome.Source);
        var sent = Assert.Single(core.Ips.Received);
        Pacs008Schema.ValidatePacs009(sent);
        var document = XDocument.Parse(sent);
        Assert.Equal("pacs.009.001.11", document.Descendants(Head + "MsgDefIdr").Single().Value);
        Assert.Equal("P9-processing", document.Descendants(Head + "BizMsgIdr").Single().Value);
        Assert.Equal("P9-processing", document.Descendants(Pacs + "MsgId").Single().Value);
        Assert.Equal("P9-processing", document.Descendants(Pacs + "TxId").Single().Value);
        Assert.Equal("E2E-009", document.Descendants(Pacs + "EndToEndId").Single().Value);
        Assert.Equal("2026-10-04", document.Descendants(Pacs + "IntrBkSttlmDt").Last().Value);
        Assert.Equal("INST", document.Descendants(Pacs + "SvcLvl").Single().Element(Pacs + "Cd")!.Value);
        Assert.Equal("INTC", document.Descendants(Pacs + "Purp").Single().Element(Pacs + "Cd")!.Value);
        Assert.Equal("Liquidity transfer", document.Descendants(Pacs + "Ustrd").Single().Value);
        Assert.Equal("GE29NB0000000101904917", document.Descendants(Pacs + "DbtrAcct").Single().Descendants(Pacs + "IBAN").Single().Value);

        // Financial institutions carry only a BICFI, and fields outside the v1 profile are never sent.
        var transaction = document.Descendants(Pacs + "CdtTrfTxInf").Single();
        Assert.Equal(["BICFI"], transaction.Element(Pacs + "Dbtr")!.Element(Pacs + "FinInstnId")!.Elements().Select(e => e.Name.LocalName));
        var names = document.Descendants().Select(element => element.Name.LocalName).ToArray();
        Assert.DoesNotContain("InstrPrty", names);
        Assert.DoesNotContain("ClrSysMmbId", names);
        Assert.DoesNotContain("InstdAgt", names);
        Assert.DoesNotContain("UETR", names);
        Assert.DoesNotContain("Othr", names);

        var stored = await core.ReadAsync(id);
        Assert.Equal("P9-processing", stored.Message.MessageId);
        Assert.Equal("pacs.009.001.11", (await Journal(core, id)).MessageDefinition);
        Assert.Equal(1, await Callbacks(core, id));
    }

    [Fact]
    public async Task Rejected_payment_is_final_with_the_ips_reason()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        core.Ips.Behavior = (reply, _) => core.Ips.RespondAsync(reply with { GroupStatus = "RJCT", TransactionStatus = "RJCT", ReasonCode = "AC01" }, "RJCT/1009");
        var id = await core.AcceptPacs009Async();

        var outcome = (await core.ProcessAsync(id))!;

        Assert.Equal(TransactionStatus.Rejected, outcome.Status);
        Assert.Equal("AC01", outcome.Details.ReasonCode);
        Assert.Equal(1, await Callbacks(core, id));
    }

    [Fact]
    public async Task Unknown_reply_leaves_the_payment_uncertain_without_a_pre_send_deadline()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost", []));
        var id = await core.AcceptPacs009Async();

        // pacs.009 has no 20 second submission window, so a late first send is still sent.
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

        var invalid = await core.AcceptAsync(Pacs009Fixture.Request("bad") with { Amount = 0, Currency = "USD" });
        Assert.Null(invalid.Intake);
        Assert.Contains(invalid.Errors, error => error.Field == "amount");
        Assert.Contains(invalid.Errors, error => error.Field == "currency");

        var first = await core.AcceptAsync(Pacs009Fixture.Request("same"));
        Assert.True(first.Intake!.Created);
        var repeat = await core.AcceptAsync(Pacs009Fixture.Request("same") with { Amount = -1 });
        Assert.False(repeat.Intake!.Created);
        Assert.Equal(first.Intake.Payment.Id, repeat.Intake.Payment.Id);
    }

    [Theory]
    [InlineData("message")]
    [InlineData("transaction")]
    public async Task An_id_already_used_by_another_payment_is_rejected(string reused)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        await core.AcceptAsync(Pacs009Fixture.Request("one") with { Id = "SHARED", TransactionId = "TX-ONE" });

        var other = Pacs009Fixture.Request("two");
        var result = await core.AcceptAsync(reused == "message"
            ? other with { Id = "SHARED", TransactionId = "TX-TWO" }
            : other with { Id = "OTHER", TransactionId = "TX-ONE" });

        Assert.Null(result.Intake);
        Assert.Contains(result.Errors, error => error.Field == "id");
    }

    [Fact]
    public async Task The_sent_message_is_signed_and_verifies_independently()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await core.AcceptPacs009Async();

        await core.ProcessAsync(id);

        var sent = Assert.Single(core.Ips.Received);
        Assert.Contains("Signature", sent);
        Assert.True(await JavaSignatureVerifier.VerifyAsync(sent, core.SigningCertificate));
        Pacs008Schema.ValidatePacs009(sent);
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
        var id = await core.AcceptPacs009Async();
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

        // Whatever was committed is reused, and the message reaches IPS exactly once across both runs.
        Assert.Equal(TransactionStatus.Accepted, outcome.Status);
        Assert.Single(core.Ips.Received);
        var resumed = await core.ReadAsync(id);
        Assert.Equal(crashed.Message.UnsignedXml ?? resumed.Message.UnsignedXml, resumed.Message.UnsignedXml);
        Assert.Equal(crashed.Message.SignedXml ?? resumed.Message.SignedXml, resumed.Message.SignedXml);
    }

    [Fact]
    public async Task Concurrent_requests_cannot_both_take_the_same_ids()
    {
        await using var core = await ProcessingHarness.CreateAsync();

        var results = await Task.WhenAll(
            core.AcceptAsync(Pacs009Fixture.Request("first") with { Id = "SAME-ID" }),
            core.AcceptAsync(Pacs009Fixture.Request("second") with { Id = "SAME-ID" }));

        Assert.Single(results, result => result.Intake is { Created: true });
        var loser = Assert.Single(results, result => result.Intake is null);
        Assert.Contains(loser.Errors, error => error.Field == "id");
    }

    [Fact]
    public async Task Stored_snapshot_resumes_with_the_exact_unsigned_xml_after_a_crash()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await core.AcceptPacs009Async();
        await Assert.ThrowsAsync<SimulatedCrash>(() => core.ProcessAsync(id, default,
            new CrashOnSave(entry => entry.Context.ChangeTracker.Entries<OutgoingMessageRow>().Any(row => row.State == EntityState.Added))));
        var crashed = await core.ReadAsync(id);
        Assert.NotNull(crashed.Message.UnsignedXml);

        core.Clock.Now += ProcessingHarness.Ownership;
        await core.RecoverAsync(id);
        var outcome = (await core.ProcessAsync(id))!;

        Assert.Equal(TransactionStatus.Accepted, outcome.Status);
        Assert.Equal(crashed.Message.UnsignedXml, (await core.ReadAsync(id)).Message.UnsignedXml);
        Assert.Single(core.Ips.Received);
    }

    private static async Task<IPS.Middleware.Application.Payments.Pacs008.OutgoingMessage> Journal(ProcessingHarness core, Guid id)
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
