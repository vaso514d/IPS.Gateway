using System.Xml.Linq;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Pain002;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Payments;

// pain.002 shares the outgoing core: same claims, journal, markers and callbacks. IPS decides it in a response header.
public sealed class Pain002ProcessingTests
{
    private static readonly XNamespace Pain = "urn:iso:std:iso:20022:tech:xsd:pain.002.001.14";
    private static readonly XNamespace Head = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";

    [Fact]
    public async Task An_accepted_refusal_is_sent_once_in_the_IPS_v1_profile_and_reported()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await core.AcceptPain002Async();

        var outcome = (await core.ProcessAsync(id))!;

        Assert.Equal(TransactionStatus.Accepted, outcome.Status);
        Assert.Equal(StatusSource.Ips, outcome.Source);
        Assert.Equal("IPS took the pain.002 refusal and forwards it to the PISP.", outcome.Details.Description);
        var sent = Assert.Single(core.Ips.Received);
        Pacs008Schema.ValidatePain002(sent);
        var document = XDocument.Parse(sent);
        Assert.Equal("pain.002.001.14", document.Descendants(Head + "MsgDefIdr").Single().Value);
        Assert.Equal("REF-processing", document.Descendants(Head + "BizMsgIdr").Single().Value);
        var group = document.Descendants(Pain + "GrpHdr").Single();
        Assert.Equal("REF-processing", group.Element(Pain + "MsgId")!.Value);
        Assert.Equal("BAGAGE22", group.Element(Pain + "DbtrAgt")!.Descendants(Pain + "BICFI").Single().Value);
        var original = document.Descendants(Pain + "OrgnlGrpInfAndSts").Single();
        Assert.Equal("PAIN001-MSG-1", original.Element(Pain + "OrgnlMsgId")!.Value);
        Assert.Equal("pain.001.001.12", original.Element(Pain + "OrgnlMsgNmId")!.Value);
        Assert.Equal("RJCT", original.Element(Pain + "GrpSts")!.Value);
        Assert.Equal("CUST", original.Element(Pain + "StsRsnInf")!.Element(Pain + "Rsn")!.Element(Pain + "Cd")!.Value);
        Assert.Null(original.Element(Pain + "StsRsnInf")!.Element(Pain + "Orgtr"));
        var payment = document.Descendants(Pain + "OrgnlPmtInfAndSts").Single();
        Assert.Equal("PMTINF-1", payment.Element(Pain + "OrgnlPmtInfId")!.Value);
        Assert.Equal("RJCT", payment.Element(Pain + "PmtInfSts")!.Value);
        var reason = payment.Element(Pain + "StsRsnInf")!;
        Assert.NotNull(reason.Element(Pain + "Orgtr"));
        Assert.Empty(reason.Element(Pain + "Orgtr")!.Elements());
        Assert.Equal("CUST", reason.Element(Pain + "Rsn")!.Element(Pain + "Cd")!.Value);
        Assert.Null(reason.Element(Pain + "AddtlInf"));

        var stored = await core.ReadAsync(id);
        Assert.Equal(("REF-processing", "REF-processing"), (stored.Message.MessageId, stored.Message.TransactionId));
        Assert.Equal("pain.002.001.14", (await Journal(core, id)).MessageDefinition);
        Assert.Equal(1, await Callbacks(core, id));
    }

    [Fact]
    public async Task The_originator_name_and_additional_information_are_sent_only_when_given()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var request = Pain002Fixture.Request("optional");
        var id = (await core.AcceptAsync(request with
        {
            OriginatorName = "Payer Bank",
            AdditionalInformation = "Account closed",
            CreatedAt = new DateTimeOffset(2026, 10, 4, 9, 30, 0, TimeSpan.Zero)
        })).Intake!.Payment.Id;

        await core.ProcessAsync(id);

        var sent = Assert.Single(core.Ips.Received);
        Pacs008Schema.ValidatePain002(sent);
        var document = XDocument.Parse(sent);
        Assert.Equal("2026-10-04T09:30:00.0000000Z", document.Descendants(Pain + "GrpHdr").Single().Element(Pain + "CreDtTm")!.Value);
        var reasons = document.Descendants(Pain + "StsRsnInf").ToArray();
        Assert.All(reasons, reason => Assert.Equal("Account closed", reason.Element(Pain + "AddtlInf")!.Value));
        Assert.Equal("Payer Bank", reasons[1].Element(Pain + "Orgtr")!.Element(Pain + "Nm")!.Value);
        Assert.Null(reasons[0].Element(Pain + "Orgtr"));
    }

    [Theory]
    [InlineData("RJCT/1017", 1017)]
    [InlineData("RJCT/1009", 1009)]
    [InlineData("RJCT", null)]
    public async Task A_rejecting_header_is_a_final_rejection_with_ips_s_code_and_a_callback(string header, int? code)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        core.Ips.Behavior = (reply, _) => core.Ips.RespondAsync(reply, header);
        var id = await core.AcceptPain002Async();

        var outcome = (await core.ProcessAsync(id))!;

        Assert.Equal(TransactionStatus.Rejected, outcome.Status);
        Assert.Equal("NARR", outcome.Details.ReasonCode);
        Assert.Equal(code, outcome.Details.IpsInternalCode);
        Assert.Contains(header, outcome.Details.Description);
        Assert.Equal(1, await Callbacks(core, id));
    }

    [Fact]
    public async Task The_header_decides_even_when_the_body_says_otherwise()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        core.Ips.Behavior = (reply, _) => core.Ips.RespondAsync(reply with { GroupStatus = "RJCT", TransactionStatus = "RJCT", ReasonCode = "AC01" }, "ACCP");
        var id = await core.AcceptPain002Async();

        var outcome = (await core.ProcessAsync(id))!;

        Assert.Equal(TransactionStatus.Accepted, outcome.Status);
    }

    [Theory]
    [InlineData(200, "PDNG")]
    [InlineData(200, "")]
    [InlineData(200, null)]
    [InlineData(503, "ACCP")]
    [InlineData(500, "RJCT/1017")]
    public async Task Any_other_answer_leaves_the_refusal_uncertain_without_a_pre_send_deadline(int httpStatus, string? header)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(httpStatus, "body", header is null ? [] : [new("X-MONTRAN-IPS-ReqSts", header)]));
        var id = await core.AcceptPain002Async();

        core.Clock.Now += TimeSpan.FromHours(2);
        var outcome = (await core.ProcessAsync(id))!;

        Assert.Equal(TransactionStatus.Uncertain, outcome.Status);
        Assert.Single(core.Ips.Received);
        Assert.Empty(core.Ips.Resent);
    }

    [Fact]
    public async Task Conflicting_request_status_headers_leave_the_refusal_uncertain()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        core.Ips.Behavior = (_, _) => Task.FromResult(new IpsSubmissionResponse(200, "body", [new("X-MONTRAN-IPS-ReqSts", "ACCP"), new("X-MONTRAN-IPS-ReqSts", "RJCT/1017")]));
        var id = await core.AcceptPain002Async();

        Assert.Equal(TransactionStatus.Uncertain, (await core.ProcessAsync(id))!.Status);
    }

    [Fact]
    public async Task Intake_reports_validation_errors_and_is_idempotent_by_client_reference()
    {
        await using var core = await ProcessingHarness.CreateAsync();

        var invalid = await core.AcceptAsync(Pain002Fixture.Request("bad") with { ReasonCode = "TOOLONG", OriginalPaymentInformationId = null });
        Assert.Null(invalid.Intake);
        Assert.Contains(invalid.Errors, error => error.Field == "reasonCode");
        Assert.Contains(invalid.Errors, error => error.Field == "originalPaymentInformationId");

        var first = await core.AcceptAsync(Pain002Fixture.Request("same"));
        Assert.True(first.Intake!.Created);
        var repeat = await core.AcceptAsync(Pain002Fixture.Request("same") with { ReasonCode = null });
        Assert.False(repeat.Intake!.Created);
        Assert.Equal(first.Intake.Payment.Id, repeat.Intake.Payment.Id);
    }

    [Fact]
    public async Task A_message_id_already_used_by_another_payment_is_rejected()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        await core.AcceptAsync(Pain002Fixture.Request("one") with { Id = "SHARED" });

        var result = await core.AcceptAsync(Pain002Fixture.Request("two") with { Id = "SHARED" });

        Assert.Null(result.Intake);
        Assert.Contains(result.Errors, error => error.Field == "id");
    }

    [Fact]
    public async Task Concurrent_requests_cannot_both_take_the_same_id()
    {
        await using var core = await ProcessingHarness.CreateAsync();

        var results = await Task.WhenAll(
            core.AcceptAsync(Pain002Fixture.Request("first") with { Id = "SAME-ID" }),
            core.AcceptAsync(Pain002Fixture.Request("second") with { Id = "SAME-ID" }));

        Assert.Single(results, result => result.Intake is { Created: true });
        var loser = Assert.Single(results, result => result.Intake is null);
        Assert.Contains(loser.Errors, error => error.Field == "id");
    }

    [Fact]
    public async Task The_sent_message_is_signed_and_verifies_independently()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await core.AcceptPain002Async();

        await core.ProcessAsync(id);

        var sent = Assert.Single(core.Ips.Received);
        Assert.Contains("Signature", sent);
        Assert.True(await JavaSignatureVerifier.VerifyAsync(sent, core.SigningCertificate));
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
        var id = await core.AcceptPain002Async();
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
    public async Task The_stored_snapshot_round_trips_with_the_refusal_content()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var id = await core.AcceptPain002Async();

        var stored = await core.ReadAsync(id);

        Assert.Contains("\"originalPaymentInformationId\":\"PMTINF-1\"", stored.AcceptedJson);
        var correlation = stored.Message.Accepted!.ReplyCorrelation("m", "t");
        Assert.Equal(("m", "PMTINF-1", "PMTINF-1", "pain.002.001.14"), (correlation.MessageId, correlation.TransactionId, correlation.EndToEndId, correlation.MessageDefinition));
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
