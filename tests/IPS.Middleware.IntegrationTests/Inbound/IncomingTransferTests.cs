using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using IPS.Middleware.Application.Inbound.Composition;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Infrastructure.Inbound;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.Middleware.Infrastructure.Inbound.Transfers;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Inbound.Workers;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Events;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Repositories.Inbound;
using IPS.Middleware.IntegrationTests.Payments;
using IPS.Middleware.IntegrationTests.Transport;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

// A pacs.009 from IPS is verified, stored once, handed to the core system and recovered if the core does not answer.
// Real SQL and independently Java-signed IPS fixtures; the core system is a simulator.
public sealed class IncomingTransferTests
{
    private const string Participant = "BAGAGE22";
    private static readonly TimeSpan Ownership = new IncomingReconciliationOptions().Ownership;

    [Fact]
    public async Task A_verified_transfer_is_stored_once_with_the_content_the_core_will_receive()
    {
        await using var core = await ProcessingHarness.CreateAsync();

        var (result, journalId) = await ApplyAsync(core, await SignedAsync(core, Unsigned(full: true)));

        Assert.Equal(IncomingCompositionStatus.Terminal, result.Status);
        Assert.Equal(InboundProcessingStatus.Processed, (await ReadReceiptAsync(core, journalId)).Status);
        var stored = await ReadOnlyAsync(core);
        Assert.Equal(CoreOutcome.NotSubmitted, stored.Transfer.CoreStatus);
        Assert.Equal(core.Clock.Now, stored.NextActionAtUtc);
        Assert.Equal(["incoming-transfer.registered"], stored.Events);
        Assert.Equal(new IncomingPacs009(
            "MSG-IN-1", "E2E-IN-1", "INSTR-1", "TX-IN-1", Guid.Parse("0f3c9c1e-9d1a-4a43-8f8e-2b1d0a1d5a11"), 1,
            new DateOnly(2026, 10, 4), "GEL", 100.5m,
            new("TBCBGE22", "MEMBER-9"), new(Participant, null),
            new(1, "Liquidity"), "INTC", "Liquidity transfer line one line two",
            "GE95TB0000000123456789", "GE29NB0000000101904917"), stored.Content);
        Assert.Equal(core.Clock.Now + new IncomingReconciliationOptions().Window, stored.DeadlineUtc);
    }

    [Fact]
    public async Task Alternative_wire_forms_map_like_the_source_does()
    {
        await using var core = await ProcessingHarness.CreateAsync();

        await ApplyAsync(core, await SignedAsync(core, Unsigned(alternate: true)));

        var content = (await ReadOnlyAsync(core)).Content;
        Assert.Equal("ACC-1", content.DebtorAccount);
        Assert.Equal(0, content.InstructionPriority);
        Assert.Equal(new IncomingCode(0, "CASH"), content.CategoryPurpose);
        Assert.Equal("Custom", content.Purpose);
        Assert.Null(content.DebtorAgent.ClearingSystemMemberId);
        Assert.Null(content.AdditionalPurpose);
        Assert.Null(content.InstructionId);
        Assert.Null(content.Uetr);
    }

    [Fact]
    public async Task A_missing_value_date_stays_absent_so_a_redelivery_on_another_day_is_the_same_transfer()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var xml = await SignedAsync(core, Unsigned().Replace("<pacs:IntrBkSttlmDt>2026-10-04</pacs:IntrBkSttlmDt>", string.Empty));
        await ApplyAsync(core, xml, sequence: 1);

        core.Clock.Now += TimeSpan.FromDays(1);
        var (again, _) = await ApplyAsync(core, xml, sequence: 2);

        Assert.Null((await ReadOnlyAsync(core)).Content.ValueDate);
        Assert.Equal(IncomingCompositionStatus.Terminal, again.Status);
    }

    public static TheoryData<string> Unverifiable() => new()
    {
        "definition", "signature", "schema", "two-transfers", "not-ours", "no-debtor-bic", "no-creditor-bic", "malformed"
    };

    [Theory]
    [MemberData(nameof(Unverifiable))]
    public async Task Anything_unverifiable_is_held_and_nothing_is_stored(string problem)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var xml = problem switch
        {
            "definition" => await SignedAsync(core, Unsigned().Replace("<head:MsgDefIdr>pacs.009.001.11", "<head:MsgDefIdr>pacs.009.001.10")),
            "signature" => await SignedAsync(core, Unsigned(), core.SigningCertificate),
            "schema" => await SignedAsync(core, Unsigned().Replace("<pacs:EndToEndId>E2E-IN-1</pacs:EndToEndId>", string.Empty)),
            "two-transfers" => await SignedAsync(core, Unsigned(transactions: 2)),
            "not-ours" => await SignedAsync(core, Unsigned(creditorBic: "TBCBGE22")),
            "no-debtor-bic" => await SignedAsync(core, Unsigned().Replace("<pacs:DbtrAgt><pacs:FinInstnId><pacs:BICFI>TBCBGE22</pacs:BICFI>", "<pacs:DbtrAgt><pacs:FinInstnId><pacs:Nm>Bank</pacs:Nm>")),
            "no-creditor-bic" => await SignedAsync(core, Unsigned().Replace("<pacs:CdtrAgt><pacs:FinInstnId><pacs:BICFI>BAGAGE22</pacs:BICFI>", "<pacs:CdtrAgt><pacs:FinInstnId><pacs:Nm>Bank</pacs:Nm>")),
            _ => "<Message>"
        };

        var (result, journalId) = await ApplyAsync(core, xml);

        Assert.Equal(IncomingCompositionStatus.Held, result.Status);
        var receipt = await ReadReceiptAsync(core, journalId);
        Assert.Equal(InboundProcessingStatus.Held, receipt.Status);
        Assert.NotNull(receipt.HoldReason);
        await using var session = core.Database.Session();
        Assert.False(await session.Context.Set<IncomingTransferMetadata>().AnyAsync());
    }

    [Fact]
    public async Task A_redelivery_is_one_transfer_and_different_content_under_the_same_key_is_held()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        await ApplyAsync(core, await SignedAsync(core, Unsigned()), sequence: 1);
        var before = await ReadOnlyAsync(core);

        var (same, _) = await ApplyAsync(core, await SignedAsync(core, Unsigned()), sequence: 2);
        var (changed, journalId) = await ApplyAsync(core, await SignedAsync(core, Unsigned(amount: "200")), sequence: 3);

        Assert.Equal(IncomingCompositionStatus.Terminal, same.Status);
        Assert.Equal(IncomingCompositionStatus.Held, changed.Status);
        Assert.Equal(IncomingPacs009Processing.ConflictReason, (await ReadReceiptAsync(core, journalId)).HoldReason);
        var after = await ReadOnlyAsync(core);
        Assert.Equal(before.Content, after.Content);
        Assert.Equal(before.Events, after.Events);
    }

    [Theory]
    [InlineData("ACCP", CoreOutcome.Accepted)]
    [InlineData("RJCT", CoreOutcome.Rejected)]
    public async Task The_core_is_asked_once_with_the_end_to_end_id_and_its_answer_is_final(string status, CoreOutcome expected)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var cbs = new TransferCore { Submit = _ => Json(status, "E2E-IN-1", reason: status == "RJCT" ? "AC01" : null) };
        var id = await RegisterTransferAsync(core);

        await ProcessAsync(core, id, cbs);
        await ProcessAsync(core, id, cbs);

        Assert.Equal([("submit", "E2E-IN-1")], cbs.Calls);
        var stored = await ReadOnlyAsync(core);
        Assert.Equal(expected, stored.Transfer.CoreStatus);
        Assert.Null(stored.NextActionAtUtc);
        Assert.Null(stored.ClaimToken);
        Assert.Equal(["incoming-transfer.registered", "incoming-transfer.submission-started", "incoming-transfer.core-outcome-recorded"], stored.Events);
        Assert.Equal(status == "RJCT" ? "AC01" : null, stored.Transfer.CoreReasonCode);
    }

    [Fact]
    public async Task A_reply_about_other_identifiers_is_not_an_answer()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var cbs = new TransferCore { Submit = _ => Json("ACCP", "OTHER") };
        var id = await RegisterTransferAsync(core);

        await ProcessAsync(core, id, cbs);

        var stored = await ReadOnlyAsync(core);
        Assert.Equal(CoreOutcome.Unknown, stored.Transfer.CoreStatus);
        Assert.Equal(core.Clock.Now + new IncomingReconciliationOptions().RetryDelay(1), stored.NextActionAtUtc);
    }

    [Fact]
    public async Task An_unanswered_submission_is_followed_by_status_questions_on_the_backoff_schedule()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var cbs = new TransferCore { Submit = _ => Task.FromResult(new CoreResponse(503, "down")), Query = _ => Json("PDNG", "E2E-IN-1") };
        var id = await RegisterTransferAsync(core);
        var options = new IncomingReconciliationOptions();
        await ProcessAsync(core, id, cbs);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var due = (await ReadOnlyAsync(core)).NextActionAtUtc!.Value;
            Assert.Equal(core.Clock.Now + options.RetryDelay(attempt), due);
            core.Clock.Now = due - TimeSpan.FromTicks(1);
            await ProcessAsync(core, id, cbs);
            Assert.Equal(attempt, cbs.Calls.Count);
            core.Clock.Now = due;
            await ProcessAsync(core, id, cbs);
            Assert.Equal(attempt + 1, cbs.Calls.Count);
        }

        Assert.Equal(("submit", "E2E-IN-1"), cbs.Calls[0]);
        Assert.All(cbs.Calls.Skip(1), call => Assert.Equal(("query", "E2E-IN-1"), call));
        Assert.Equal(
            [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15)],
            Enumerable.Range(1, 5).Select(options.RetryDelay));
    }

    [Fact]
    public async Task A_later_answer_settles_the_transfer()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var cbs = new TransferCore { Submit = _ => Task.FromResult(new CoreResponse(503, "down")), Query = _ => Json("ACCP", "E2E-IN-1") };
        var id = await RegisterTransferAsync(core);
        await ProcessAsync(core, id, cbs);

        core.Clock.Now += new IncomingReconciliationOptions().RetryDelay(1);
        await ProcessAsync(core, id, cbs);

        Assert.Equal(CoreOutcome.Accepted, (await ReadOnlyAsync(core)).Transfer.CoreStatus);
        Assert.Equal([("submit", "E2E-IN-1"), ("query", "E2E-IN-1")], cbs.Calls);
    }

    [Fact]
    public async Task A_core_that_never_saw_the_transfer_gets_the_same_request_again_under_the_same_key()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var cbs = new TransferCore
        {
            Submit = _ => Task.FromResult(new CoreResponse(503, "down")),
            Query = _ => Task.FromResult(new CoreResponse(404, "unknown reference"))
        };
        var id = await RegisterTransferAsync(core);
        await ProcessAsync(core, id, cbs);
        core.Clock.Now += new IncomingReconciliationOptions().RetryDelay(1);

        await ProcessAsync(core, id, cbs);

        var afterQuery = await ReadOnlyAsync(core);
        Assert.Equal(CoreOutcome.NotSubmitted, afterQuery.Transfer.CoreStatus);
        Assert.Equal(core.Clock.Now, afterQuery.NextActionAtUtc);
        cbs.Submit = _ => Json("ACCP", "E2E-IN-1");
        await ProcessAsync(core, id, cbs);

        Assert.Equal(CoreOutcome.Accepted, (await ReadOnlyAsync(core)).Transfer.CoreStatus);
        Assert.Equal([("submit", "E2E-IN-1"), ("query", "E2E-IN-1"), ("submit", "E2E-IN-1")], cbs.Calls);
        Assert.Equal(cbs.Transfers[0], cbs.Transfers[1]);
    }

    [Fact]
    public async Task The_window_end_goes_to_manual_review_without_another_call()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var cbs = new TransferCore { Submit = _ => Task.FromResult(new CoreResponse(503, "down")), Query = _ => Task.FromResult(new CoreResponse(503, "down")) };
        var id = await RegisterTransferAsync(core);
        await ProcessAsync(core, id, cbs);
        var window = new IncomingReconciliationOptions().Window;
        core.Clock.Now = ProcessingHarness.Start + window - TimeSpan.FromTicks(1);
        await ProcessAsync(core, id, cbs);
        var before = cbs.Calls.Count;
        core.Clock.Now = ProcessingHarness.Start + window;

        await ProcessAsync(core, id, cbs);

        var stored = await ReadOnlyAsync(core);
        Assert.NotNull(stored.Transfer.ManualReviewReason);
        Assert.Null(stored.NextActionAtUtc);
        Assert.Equal(before, cbs.Calls.Count);
        Assert.Equal("incoming-transfer.manual-review-required", stored.Events[^1]);
    }

    [Fact]
    public async Task A_competing_owner_cannot_call_the_core_while_the_first_owner_holds_the_transfer()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cbs = new TransferCore { Submit = async _ => { await gate.Task; return await Accept(); } };
        var id = await RegisterTransferAsync(core);
        var first = ProcessAsync(core, id, cbs);
        await WaitUntilAsync(() => cbs.Calls.Count == 1);

        await ProcessAsync(core, id, cbs);
        Assert.Single(cbs.Calls);

        gate.SetResult();
        await first;
        Assert.Equal(CoreOutcome.Accepted, (await ReadOnlyAsync(core)).Transfer.CoreStatus);
        Assert.Single(cbs.Calls);
    }

    [Fact]
    public async Task A_stale_owner_cannot_overwrite_the_next_owners_outcome()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cbs = new TransferCore
        {
            Submit = async _ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    await gate.Task;
                }

                return await Accept();
            },
            Query = _ => Accept()
        };
        var id = await RegisterTransferAsync(core);
        var stale = ProcessAsync(core, id, cbs);
        await WaitUntilAsync(() => cbs.Calls.Count == 1);

        core.Clock.Now += Ownership + TimeSpan.FromSeconds(1);
        await ProcessAsync(core, id, cbs);
        gate.SetResult();
        await stale;

        var stored = await ReadOnlyAsync(core);
        Assert.Equal(CoreOutcome.Accepted, stored.Transfer.CoreStatus);
        Assert.Single(stored.Events, name => name == "incoming-transfer.core-outcome-recorded");
        Assert.Null(stored.ClaimToken);
    }

    [Fact]
    public async Task A_crash_before_the_outcome_commits_is_recovered_by_asking_the_core_not_by_sending_again()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var cbs = new TransferCore { Submit = _ => Json("ACCP", "E2E-IN-1"), Query = _ => Json("ACCP", "E2E-IN-1") };
        var id = await RegisterTransferAsync(core);

        await Assert.ThrowsAsync<SimulatedCrash>(() => ProcessAsync(core, id, cbs, new CrashWhenOutcomeIsSaved()));

        var crashed = await ReadOnlyAsync(core);
        Assert.Equal(CoreOutcome.SubmissionStarted, crashed.Transfer.CoreStatus);
        core.Clock.Now += Ownership + TimeSpan.FromSeconds(1);
        await ProcessAsync(core, id, cbs);

        Assert.Equal(CoreOutcome.Accepted, (await ReadOnlyAsync(core)).Transfer.CoreStatus);
        Assert.Equal([("submit", "E2E-IN-1"), ("query", "E2E-IN-1")], cbs.Calls);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(500)]
    public async Task Worker_pulls_stores_acknowledges_and_hands_the_transfer_to_the_core_even_when_the_ack_fails(int ackStatus)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var xml = await SignedAsync(core, Unsigned());
        var deliveries = new ConcurrentQueue<(string Sequence, string Body)>([("7", xml), ("7", xml)]);
        var acknowledgements = new ConcurrentQueue<string?>();
        var submissions = new ConcurrentQueue<(string? Key, string Body)>();
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            if (context.Request.Method == "GET" && context.Request.Path == "/Message")
            {
                if (deliveries.TryDequeue(out var delivery))
                {
                    context.Response.Headers["X-MONTRAN-IPS-MessageSeq"] = delivery.Sequence;
                    context.Response.Headers["X-MONTRAN-IPS-MessageType"] = "pacs.009";
                    await context.Response.WriteAsync(delivery.Body);
                }
                else
                {
                    context.Response.Headers["X-MONTRAN-IPS-ReqSts"] = "EMPTY";
                }
            }
            else if (context.Request.Method == "POST" && context.Request.Path == "/MessageAck")
            {
                acknowledgements.Enqueue(context.Request.Headers["X-MONTRAN-IPS-MessageSeq"]);
                context.Response.StatusCode = ackStatus;
            }
            else if (context.Request.Method == "POST" && context.Request.Path == "/api/ips/pacs009/receive")
            {
                var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
                submissions.Enqueue((context.Request.Headers["Idempotency-Key"], body));
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"status\":\"ACCP\",\"endToEndId\":\"E2E-IN-1\"}");
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
            await EventuallyAsync(async () => await HasAcceptedTransferAsync(core));
            await EventuallyAsync(() => Task.FromResult(acknowledgements.Count == 2));
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Equal(["7", "7"], acknowledgements);
        var submission = Assert.Single(submissions);
        Assert.Equal("E2E-IN-1", submission.Key);
        Assert.Contains("\"EndToEndId\":\"E2E-IN-1\"", submission.Body);
        Assert.Contains("\"Id\":\"MSG-IN-1\"", submission.Body);
    }

    private static string Unsigned(bool full = false, string creditorBic = Participant, int transactions = 1, string amount = "100.5", bool alternate = false)
    {
        const string head = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";
        const string pacs = "urn:iso:std:iso:20022:tech:xsd:pacs.009.001.11";
        static string Agent(string bic, string? member = null) =>
            $"<pacs:FinInstnId><pacs:BICFI>{bic}</pacs:BICFI>" +
            (member is null ? string.Empty : $"<pacs:ClrSysMmbId><pacs:ClrSysId><pacs:Cd>GE</pacs:Cd></pacs:ClrSysId><pacs:MmbId>{member}</pacs:MmbId></pacs:ClrSysMmbId>") +
            "</pacs:FinInstnId>";
        static string Account(string iban) => $"<pacs:Id><pacs:IBAN>{iban}</pacs:IBAN></pacs:Id>";
        var debtorAccount = alternate ? "<pacs:Id><pacs:Othr><pacs:Id>ACC-1</pacs:Id></pacs:Othr></pacs:Id>" : Account("GE95TB0000000123456789");
        var debtorAgent = Agent("TBCBGE22", full ? "MEMBER-9" : null);
        var priority = full ? "<pacs:InstrPrty>HIGH</pacs:InstrPrty>" : alternate ? "<pacs:InstrPrty>NORM</pacs:InstrPrty>" : string.Empty;
        var categoryPurpose = full ? "<pacs:CtgyPurp><pacs:Prtry>Liquidity</pacs:Prtry></pacs:CtgyPurp>"
            : alternate ? "<pacs:CtgyPurp><pacs:Cd>CASH</pacs:Cd></pacs:CtgyPurp>" : string.Empty;
        var instructionId = full ? "<pacs:InstrId>INSTR-1</pacs:InstrId>" : string.Empty;
        var uetr = full ? "<pacs:UETR>0f3c9c1e-9d1a-4a43-8f8e-2b1d0a1d5a11</pacs:UETR>" : string.Empty;
        var extras = full
            ? "<pacs:Purp><pacs:Cd>INTC</pacs:Cd></pacs:Purp><pacs:RmtInf><pacs:Ustrd>Liquidity transfer line one </pacs:Ustrd><pacs:Ustrd>line two</pacs:Ustrd></pacs:RmtInf>"
            : alternate ? "<pacs:Purp><pacs:Prtry>Custom</pacs:Prtry></pacs:Purp>" : string.Empty;
        var transaction =
            $"<pacs:CdtTrfTxInf><pacs:PmtId>{instructionId}<pacs:EndToEndId>E2E-IN-1</pacs:EndToEndId><pacs:TxId>TX-IN-1</pacs:TxId>{uetr}</pacs:PmtId>" +
            $"<pacs:IntrBkSttlmAmt Ccy=\"GEL\">{amount}</pacs:IntrBkSttlmAmt><pacs:IntrBkSttlmDt>2026-10-04</pacs:IntrBkSttlmDt>" +
            $"<pacs:Dbtr>{debtorAgent}</pacs:Dbtr><pacs:DbtrAcct>{debtorAccount}</pacs:DbtrAcct><pacs:DbtrAgt>{debtorAgent}</pacs:DbtrAgt>" +
            $"<pacs:CdtrAgt>{Agent(creditorBic)}</pacs:CdtrAgt><pacs:Cdtr>{Agent(creditorBic)}</pacs:Cdtr><pacs:CdtrAcct>{Account("GE29NB0000000101904917")}</pacs:CdtrAcct>" +
            $"{extras}</pacs:CdtTrfTxInf>";
        return
            $"<Message xmlns:head=\"{head}\" xmlns:pacs=\"{pacs}\"><head:AppHdr>" +
            "<head:Fr><head:FIId><head:FinInstnId><head:BICFI>NBGEGE22</head:BICFI></head:FinInstnId></head:FIId></head:Fr>" +
            $"<head:To><head:FIId><head:FinInstnId><head:BICFI>{Participant}</head:BICFI></head:FinInstnId></head:FIId></head:To>" +
            "<head:BizMsgIdr>MSG-IN-1</head:BizMsgIdr><head:MsgDefIdr>pacs.009.001.11</head:MsgDefIdr><head:CreDt>2026-10-04T10:00:00.0000000Z</head:CreDt><head:Sgntr/></head:AppHdr>" +
            $"<pacs:Document><pacs:FICdtTrf><pacs:GrpHdr><pacs:MsgId>MSG-IN-1</pacs:MsgId><pacs:CreDtTm>2026-10-04T10:00:00.0000000Z</pacs:CreDtTm>" +
            $"<pacs:NbOfTxs>{transactions}</pacs:NbOfTxs><pacs:TtlIntrBkSttlmAmt Ccy=\"GEL\">{amount}</pacs:TtlIntrBkSttlmAmt><pacs:IntrBkSttlmDt>2026-10-04</pacs:IntrBkSttlmDt>" +
            "<pacs:SttlmInf><pacs:SttlmMtd>CLRG</pacs:SttlmMtd><pacs:ClrSys><pacs:Cd>IPS</pacs:Cd></pacs:ClrSys></pacs:SttlmInf>" +
            $"<pacs:PmtTpInf>{priority}<pacs:SvcLvl><pacs:Cd>INST</pacs:Cd></pacs:SvcLvl><pacs:LclInstrm><pacs:Cd>INST</pacs:Cd></pacs:LclInstrm>{categoryPurpose}</pacs:PmtTpInf>" +
            "<pacs:InstgAgt><pacs:FinInstnId><pacs:BICFI>TBCBGE22</pacs:BICFI></pacs:FinInstnId></pacs:InstgAgt></pacs:GrpHdr>" +
            string.Concat(Enumerable.Repeat(transaction, transactions)) +
            "</pacs:FICdtTrf></pacs:Document></Message>";
    }

    private static async Task<string> SignedAsync(ProcessingHarness core, string unsigned, X509Certificate2? signer = null) =>
        (await IpsReplies.SignAsync(signer ?? core.IpsCertificate, unsigned))[0];

    private static async Task<(IncomingCompositionResult Result, Guid JournalId)> ApplyAsync(ProcessingHarness core, string xml, long sequence = 1)
    {
        Guid journalId;
        await using (var registering = core.Database.Session())
        {
            var registration = await new InboundReceiptRepository(registering.Context)
                .StageRegistrationAsync(new(Participant, sequence, "pacs.009", xml, false, core.Clock.Now), default);
            await registering.Unit.SaveAsync();
            journalId = registration.JournalId;
        }

        InboundClaim claim;
        await using (var claiming = core.Database.Session())
        {
            var work = new InboundWork(new InboundWorkRepository(claiming.Context), claiming.Unit, core.Clock);
            claim = (await work.AcquireAsync(journalId, TimeSpan.FromSeconds(45), default))!;
        }

        await using var session = core.Database.Session();
        var receipt = (await new InboundReceiptRepository(session.Context).ReadAsync(journalId, default))!.Receipt;
        var handler = new IncomingPacs009Processing(
            new IncomingPacs009Protocol([core.IpsCertificate]),
            new IncomingTransferRepository(session.Context),
            new InboundWorkRepository(session.Context),
            session.Unit,
            new IncomingReconciliationOptions(),
            core.Clock);
        return (await handler.ProcessAsync(claim, receipt, default), journalId);
    }

    private static async Task<Guid> RegisterTransferAsync(ProcessingHarness core)
    {
        await ApplyAsync(core, await SignedAsync(core, Unsigned()));
        return (await ReadOnlyAsync(core)).Transfer.Id;
    }

    private static async Task ProcessAsync(
        ProcessingHarness core,
        Guid transferId,
        TransferCore cbs,
        IInterceptor? interceptor = null)
    {
        await using var session = core.Database.Session(interceptor is null ? [] : [interceptor]);
        await new IncomingTransferProcessing(
                new IncomingTransferRepository(session.Context), session.Unit, cbs, new IncomingCoreReplyInterpreter(), new IncomingReconciliationOptions(), core.Clock)
            .ProcessAsync(transferId, default);
    }

    private static async Task<StoredTransfer> ReadOnlyAsync(ProcessingHarness core)
    {
        await using var session = core.Database.Session();
        var metadata = await session.Context.Set<IncomingTransferMetadata>().Include(x => x.Transfer).SingleAsync();
        var events = await session.Context.Set<TransactionEventRow>()
            .Where(row => row.TransactionId == metadata.Id)
            .OrderBy(row => row.Sequence)
            .Select(row => row.Name)
            .ToListAsync();
        var snapshot = (await new IncomingTransferRepository(session.Context).ReadAsync(metadata.Id, default))!;
        return new(metadata.Transfer, snapshot.Content, snapshot.DeadlineUtc, metadata.NextActionAtUtc, metadata.ClaimToken, events);
    }

    private static async Task<bool> HasAcceptedTransferAsync(ProcessingHarness core)
    {
        await using var session = core.Database.Session();
        return await session.Context.Set<IncomingFiTransfer>().AnyAsync(transfer => transfer.CoreStatus == CoreOutcome.Accepted);
    }

    private static async Task<StoredInboundReceipt> ReadReceiptAsync(ProcessingHarness core, Guid journalId)
    {
        await using var session = core.Database.Session();
        return (await new InboundReceiptRepository(session.Context).ReadAsync(journalId, default))!;
    }

    private static Task<CoreResponse> Accept() => Json("ACCP", "E2E-IN-1");

    private static Task<CoreResponse> Json(string status, string endToEndId, string? reason = null) =>
        Task.FromResult(new CoreResponse(200, $"{{\"status\":\"{status}\",\"endToEndId\":\"{endToEndId}\"" + (reason is null ? string.Empty : $",\"reasonCode\":\"{reason}\"") + "}"));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "The expected call was not observed.");
            await Task.Delay(20);
        }
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
            ParticipantBic = Participant,
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

    private sealed record StoredTransfer(
        IncomingFiTransfer Transfer,
        IncomingPacs009 Content,
        DateTimeOffset DeadlineUtc,
        DateTimeOffset? NextActionAtUtc,
        Guid? ClaimToken,
        IReadOnlyList<string> Events);

    // The core system: records each call and answers as scripted; by default every submission is accepted.
    private sealed class TransferCore : IIncomingTransferCoreClient
    {
        private readonly object _gate = new();
        private readonly List<(string Kind, string EndToEndId)> _calls = [];
        private readonly List<IncomingPacs009> _transfers = [];

        public Func<IncomingPacs009, Task<CoreResponse>> Submit { get; set; } = _ => Accept();
        public Func<string, Task<CoreResponse>> Query { get; set; } = _ => Accept();

        public IReadOnlyList<(string Kind, string EndToEndId)> Calls
        {
            get
            {
                lock (_gate)
                {
                    return _calls.ToArray();
                }
            }
        }

        public IReadOnlyList<IncomingPacs009> Transfers
        {
            get
            {
                lock (_gate)
                {
                    return _transfers.ToArray();
                }
            }
        }

        Task<CoreResponse> IIncomingTransferCoreClient.SubmitAsync(string participantBic, IncomingPacs009 transfer, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _calls.Add(("submit", transfer.EndToEndId));
                _transfers.Add(transfer);
            }

            return Submit(transfer);
        }

        Task<CoreResponse> IIncomingTransferCoreClient.QueryAsync(string participantBic, string endToEndId, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _calls.Add(("query", endToEndId));
            }

            return Query(endToEndId);
        }
    }

    // Fails the save that would record the core's final answer, after the call was made.
    private sealed class CrashWhenOutcomeIsSaved : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            eventData.Context!.ChangeTracker.Entries<IncomingFiTransfer>().Any(entry => entry.Entity.CoreStatus == CoreOutcome.Accepted)
                ? throw new SimulatedCrash()
                : ValueTask.FromResult(result);
    }
}
