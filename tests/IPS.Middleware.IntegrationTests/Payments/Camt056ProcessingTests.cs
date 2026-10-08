using System.Xml.Linq;
using IPS.Middleware.Application.Payments.Camt056;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Recalls;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Payments;

// camt.056 shares the outgoing core: same claims, journal, markers and callbacks, with its own content and correlation.
public sealed class Camt056ProcessingTests
{
    private static readonly XNamespace Camt = "urn:iso:std:iso:20022:tech:xsd:camt.056.001.11";
    private static readonly XNamespace Head = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";

    [Fact]
    public async Task Accepted_recall_is_sent_once_in_the_IPS_v1_profile_and_reported()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await core.AcceptCamt056Async();

        var outcome = (await core.ProcessAsync(id))!;

        Assert.Equal(TransactionStatus.Accepted, outcome.Status);
        Assert.Equal(StatusSource.Ips, outcome.Source);
        var sent = Assert.Single(core.Ips.Received);
        Pacs008Schema.ValidateCamt056(sent);
        var document = XDocument.Parse(sent);
        Assert.Equal("camt.056.001.11", document.Descendants(Head + "MsgDefIdr").Single().Value);
        Assert.Equal("RCL-processing", document.Descendants(Head + "BizMsgIdr").Single().Value);
        var assignment = document.Descendants(Camt + "Assgnmt").Single();
        Assert.Equal("RCL-processing", assignment.Element(Camt + "Id")!.Value);
        Assert.Equal("BAGAGE22", assignment.Element(Camt + "Assgnr")!.Descendants(Camt + "BICFI").Single().Value);
        Assert.Equal("NBGEGE22", assignment.Element(Camt + "Assgne")!.Descendants(Camt + "BICFI").Single().Value);
        var transaction = document.Descendants(Camt + "TxInf").Single();
        Assert.Equal("CXL-processing", transaction.Element(Camt + "CxlId")!.Value);
        Assert.Equal("ORIG-MSG-1", transaction.Element(Camt + "OrgnlGrpInf")!.Element(Camt + "OrgnlMsgId")!.Value);
        Assert.Equal("pacs.008.001.12", transaction.Element(Camt + "OrgnlGrpInf")!.Element(Camt + "OrgnlMsgNmId")!.Value);
        Assert.Equal("ORIG-E2E-1", transaction.Element(Camt + "OrgnlEndToEndId")!.Value);
        Assert.Equal("ORIG-TX-1", transaction.Element(Camt + "OrgnlTxId")!.Value);
        Assert.Equal("100.5", transaction.Element(Camt + "OrgnlIntrBkSttlmAmt")!.Value);
        Assert.Equal("2026-10-03", transaction.Element(Camt + "OrgnlIntrBkSttlmDt")!.Value);
        var reason = transaction.Element(Camt + "CxlRsnInf")!;
        Assert.Equal("BAGAGE22", reason.Descendants(Camt + "AnyBIC").Single().Value);
        Assert.Equal("DUPL", reason.Element(Camt + "Rsn")!.Element(Camt + "Cd")!.Value);
        var reference = transaction.Element(Camt + "OrgnlTxRef")!;
        Assert.Equal("CLRG", reference.Element(Camt + "SttlmInf")!.Element(Camt + "SttlmMtd")!.Value);
        Assert.Equal("INST", reference.Element(Camt + "PmtTpInf")!.Element(Camt + "SvcLvl")!.Element(Camt + "Cd")!.Value);
        Assert.Equal("BAGAGE22", reference.Element(Camt + "DbtrAgt")!.Descendants(Camt + "BICFI").Single().Value);
        Assert.Equal("TBCBGE22", reference.Element(Camt + "CdtrAgt")!.Descendants(Camt + "BICFI").Single().Value);
        Assert.Equal("GE29NB0000000101904917", reference.Element(Camt + "DbtrAcct")!.Descendants(Camt + "IBAN").Single().Value);
        var names = document.Descendants().Select(element => element.Name.LocalName).ToArray();
        Assert.DoesNotContain("RmtInf", names);
        Assert.DoesNotContain("UltmtDbtr", names);
        Assert.DoesNotContain("PstlAdr", names);

        var stored = await core.ReadAsync(id);
        Assert.Equal(("RCL-processing", "CXL-processing"), (stored.Message.MessageId, stored.Message.TransactionId));
        Assert.Equal("camt.056.001.11", (await Journal(core, id)).MessageDefinition);
        Assert.Equal(1, await Callbacks(core, id));
    }

    [Fact]
    public async Task Optional_content_is_sent_only_when_given()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var request = Camt056Fixture.Request("optional");
        var original = request.OriginalTransaction!;
        var id = (await core.AcceptAsync(request with
        {
            CreatedAt = new DateTimeOffset(2026, 10, 4, 9, 30, 0, TimeSpan.Zero),
            OriginalTransaction = original with
            {
                Remittance = new() { Unstructured = "Refund", CreditorReference = new() { Issuer = "Bank", Reference = "RF18" } },
                UltimateDebtor = new() { Name = "Ultimate Payer", Type = 1, Identifier = "01001" },
                UltimateCreditor = new() { Name = "Ultimate Payee", Type = 0, Identifier = "123456789" },
                Debtor = original.Debtor! with
                {
                    Type = 0,
                    Identifier = "987654321",
                    Address = new() { StreetName = "Rustaveli", TownName = "Tbilisi", Country = "GE", AddressLines = ["Floor 2", "Room 5"] }
                }
            }
        })).Intake!.Payment.Id;

        await core.ProcessAsync(id);

        var sent = Assert.Single(core.Ips.Received);
        Pacs008Schema.ValidateCamt056(sent);
        var document = XDocument.Parse(sent);
        Assert.Equal("2026-10-04T09:30:00.0000000Z", document.Descendants(Camt + "Assgnmt").Single().Element(Camt + "CreDtTm")!.Value);
        var reference = document.Descendants(Camt + "OrgnlTxRef").Single();
        var remittance = reference.Element(Camt + "RmtInf")!;
        Assert.Equal("Refund", remittance.Element(Camt + "Ustrd")!.Value);
        var creditorReference = remittance.Descendants(Camt + "CdtrRefInf").Single();
        Assert.Equal("SCOR", creditorReference.Descendants(Camt + "Cd").Single().Value);
        Assert.Equal("Bank", creditorReference.Descendants(Camt + "Issr").Single().Value);
        Assert.Equal("RF18", creditorReference.Element(Camt + "Ref")!.Value);
        Assert.Equal("01001", reference.Element(Camt + "UltmtDbtr")!.Descendants(Camt + "PrvtId").Single().Descendants(Camt + "Id").Single().Value);
        Assert.Equal("123456789", reference.Element(Camt + "UltmtCdtr")!.Descendants(Camt + "OrgId").Single().Descendants(Camt + "Id").Single().Value);
        var debtor = reference.Element(Camt + "Dbtr")!.Element(Camt + "Pty")!;
        Assert.Equal("987654321", debtor.Element(Camt + "Id")!.Descendants(Camt + "OrgId").Single().Descendants(Camt + "Id").Single().Value);
        var address = debtor.Element(Camt + "PstlAdr")!;
        Assert.Equal(["StrtNm", "TwnNm", "Ctry", "AdrLine", "AdrLine"], address.Elements().Select(element => element.Name.LocalName));
    }

    [Fact]
    public async Task Only_a_reply_about_the_recalled_payment_settles_the_recall()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        core.Ips.Behavior = (reply, _) => core.Ips.RespondAsync(reply with { TransactionId = "CXL-processing", EndToEndId = "OTHER" }, "ACCP");
        var id = await core.AcceptCamt056Async();

        var outcome = (await core.ProcessAsync(id))!;

        // The reply echoes the recall id instead of the recalled payment's ids, so it does not belong to this recall.
        Assert.Equal(TransactionStatus.Uncertain, outcome.Status);
    }

    [Fact]
    public async Task Rejected_recall_is_final_with_the_ips_reason()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        core.Ips.Behavior = (reply, _) => core.Ips.RespondAsync(reply with { GroupStatus = "RJCT", TransactionStatus = "RJCT", ReasonCode = "AC01" }, "RJCT/1009");
        var id = await core.AcceptCamt056Async();

        var outcome = (await core.ProcessAsync(id))!;

        Assert.Equal(TransactionStatus.Rejected, outcome.Status);
        Assert.Equal("AC01", outcome.Details.ReasonCode);
        Assert.Equal(1, await Callbacks(core, id));
    }

    [Fact]
    public async Task Unknown_reply_leaves_the_recall_uncertain_without_a_pre_send_deadline()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(503, "lost", []));
        var id = await core.AcceptCamt056Async();

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

        var invalid = await core.AcceptAsync(Camt056Fixture.Request("bad") with { OriginalAmount = 0, ReasonCode = "TOOLONG", OriginalSettlementDate = new DateOnly(2027, 1, 1) });
        Assert.Null(invalid.Intake);
        Assert.Contains(invalid.Errors, error => error.Field == "originalAmount");
        Assert.Contains(invalid.Errors, error => error.Field == "reasonCode");
        Assert.Contains(invalid.Errors, error => error.Field == "originalSettlementDate");

        var first = await core.AcceptAsync(Camt056Fixture.Request("same"));
        Assert.True(first.Intake!.Created);
        var repeat = await core.AcceptAsync(Camt056Fixture.Request("same") with { OriginalAmount = -1 });
        Assert.False(repeat.Intake!.Created);
        Assert.Equal(first.Intake.Payment.Id, repeat.Intake.Payment.Id);
    }

    [Theory]
    [InlineData("message")]
    [InlineData("recall")]
    public async Task An_id_already_used_by_another_payment_is_rejected(string reused)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        await core.AcceptAsync(Camt056Fixture.Request("one") with { Id = "SHARED", RecallId = "CXL-ONE" });

        var other = Camt056Fixture.Request("two");
        var result = await core.AcceptAsync(reused == "message"
            ? other with { Id = "SHARED", RecallId = "CXL-TWO" }
            : other with { Id = "OTHER", RecallId = "CXL-ONE" });

        Assert.Null(result.Intake);
        Assert.Contains(result.Errors, error => error.Field == "id");
    }

    [Fact]
    public async Task A_recall_id_used_by_a_pacs009_message_id_is_rejected()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        await core.AcceptAsync(Pacs009Fixture.Request("nine") with { Id = "SHARED" });

        var result = await core.AcceptAsync(Camt056Fixture.Request("recall") with { RecallId = "SHARED" });

        Assert.Null(result.Intake);
        Assert.Contains(result.Errors, error => error.Field == "id");
    }

    [Fact]
    public async Task Concurrent_requests_cannot_both_take_the_same_ids()
    {
        await using var core = await ProcessingHarness.CreateAsync();

        var results = await Task.WhenAll(
            core.AcceptAsync(Camt056Fixture.Request("first") with { Id = "SAME-ID" }),
            core.AcceptAsync(Camt056Fixture.Request("second") with { Id = "SAME-ID" }));

        Assert.Single(results, result => result.Intake is { Created: true });
        var loser = Assert.Single(results, result => result.Intake is null);
        Assert.Contains(loser.Errors, error => error.Field == "id");
    }

    [Fact]
    public async Task The_sent_message_is_signed_and_verifies_independently()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await core.AcceptCamt056Async();

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
        var id = await core.AcceptCamt056Async();
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
    public async Task The_stored_snapshot_round_trips_with_the_recall_content()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await core.AcceptCamt056Async();

        var stored = await core.ReadAsync(id);

        Assert.Contains("\"recallId\":\"CXL-processing\"", stored.AcceptedJson);
        var correlation = stored.Message.Accepted!.ReplyCorrelation("m", "t");
        Assert.Equal(("m", "ORIG-TX-1", "ORIG-E2E-1", "camt.056.001.11"), (correlation.MessageId, correlation.TransactionId, correlation.EndToEndId, correlation.MessageDefinition));
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
