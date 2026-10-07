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
using IPS.Middleware.IntegrationTests.Diagnostics;
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
[Collection("Metrics")]
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

        var content = (await ReadOnlyAsync(core)).Pacs009;
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

        Assert.Null((await ReadOnlyAsync(core)).Pacs009.ValueDate);
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

    public static TheoryData<string, string, bool> TransfersAtValidityBounds()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var messageType in new[] { "pacs.009", "pacs.004", "pain.001" })
        {
            foreach (var moment in SignatureValidity.Moments())
            {
                data.Add(messageType, (string)moment[0], (bool)moment[1]);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(TransfersAtValidityBounds))]
    // Judged as of the receipt: processing after the certificate expired does not matter.
    public async Task A_transfer_received_outside_its_certificate_validity_is_held_and_nothing_is_stored(string messageType, string receivedAt, bool valid)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var unsigned = messageType switch
        {
            "pacs.009" => Unsigned(),
            "pacs.004" => UnsignedReturn(),
            _ => UnsignedInitiation()
        };
        var xml = await SignedAsync(core, unsigned);
        core.Clock.Now = SignatureValidity.LongAfterExpiry(core.IpsCertificate);

        var (result, journalId) = await ApplyAsync(core, xml, messageType: messageType, receivedAt: SignatureValidity.At(core.IpsCertificate, receivedAt));

        await using var session = core.Database.Session();
        var stored = await session.Context.Set<IncomingTransferMetadata>().AnyAsync();
        if (valid)
        {
            Assert.Equal(IncomingCompositionStatus.Terminal, result.Status);
            Assert.True(stored);
        }
        else
        {
            Assert.Equal(IncomingCompositionStatus.Held, result.Status);
            var receipt = await ReadReceiptAsync(core, journalId);
            Assert.Equal(InboundProcessingStatus.Held, receipt.Status);
            Assert.Equal(SignatureValidity.Stored(SignatureValidity.IncomingHold(core.IpsCertificate)), receipt.HoldReason);
            Assert.False(stored);
        }
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
        Assert.Equal(IncomingTransferRegistration.ConflictReason, (await ReadReceiptAsync(core, journalId)).HoldReason);
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
    public async Task A_pacs009_without_a_transaction_id_accepts_an_answer_that_names_one()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        await ApplyAsync(core, await SignedAsync(core, Unsigned().Replace("<pacs:TxId>TX-IN-1</pacs:TxId>", string.Empty)));
        var id = (await ReadOnlyAsync(core)).Transfer.Id;
        var cbs = new TransferCore
        {
            Submit = _ => Task.FromResult(new CoreResponse(200, "{\"status\":\"ACCP\",\"endToEndId\":\"E2E-IN-1\",\"txId\":\"CORE-TX\"}"))
        };

        await ProcessAsync(core, id, cbs);

        Assert.Null(((IncomingPacs009)(await ReadOnlyAsync(core)).Content).TransactionId);
        Assert.Equal(CoreOutcome.Accepted, (await ReadOnlyAsync(core)).Transfer.CoreStatus);
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

    [Fact]
    public async Task A_verified_return_is_stored_with_the_content_the_core_will_receive()
    {
        await using var core = await ProcessingHarness.CreateAsync();

        var (result, journalId) = await ApplyAsync(core, await SignedAsync(core, UnsignedReturn(full: true)), messageType: "pacs.004");

        Assert.Equal(IncomingCompositionStatus.Terminal, result.Status);
        Assert.Equal(InboundProcessingStatus.Processed, (await ReadReceiptAsync(core, journalId)).Status);
        var stored = await ReadOnlyAsync(core);
        Assert.Equal(("pacs.004", "RTR-IN-1"), (stored.Transfer.Kind, stored.Transfer.Key));
        Assert.Equal(CoreOutcome.NotSubmitted, stored.Transfer.CoreStatus);
        Assert.Equal(["incoming-transfer.registered"], stored.Events);
        Assert.Equal(new IncomingPacs004(
            "RTR-IN-1", 100.5m, "GEL", new DateOnly(2026, 10, 4), "FOCR", "Returned in full", Participant, "TBCBGE22", "MEMBER-9",
            new("Original Payer", 0, "123456789", "GE95TB0000000123456789"),
            new("Original Payee", 1, "01001", "GE29NB0000000101904917"),
            new("ORIG-TX-1", "ORIG-INSTR-1", "ORIG-E2E-1", Guid.Parse("0f3c9c1e-9d1a-4a43-8f8e-2b1d0a1d5a11"), new DateOnly(2026, 10, 3),
                "M-ORIG", "pacs.008.001.12", new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero), 100.5m, "GEL")), stored.Content);
    }

    [Fact]
    public async Task A_return_with_only_the_required_content_maps_the_optional_parts_as_absent()
    {
        await using var core = await ProcessingHarness.CreateAsync();

        await ApplyAsync(core, await SignedAsync(core, UnsignedReturn()), messageType: "pacs.004");

        var content = (await ReadOnlyAsync(core)).Pacs004;
        Assert.Null(content.SenderMemberId);
        Assert.Null(content.AdditionalInformation);
        Assert.Equal(new IncomingReturnParty("Original Payer", null, null, "GE95TB0000000123456789"), content.Debtor);
        Assert.Equal(new IncomingOriginalPayment("ORIG-TX-1", null, "ORIG-E2E-1", null, new DateOnly(2026, 10, 3), null, null, null, 100.5m, "GEL"), content.Original);
    }

    [Fact]
    public async Task Alternative_return_wire_forms_map_like_the_source_does()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var unsigned = UnsignedReturn(full: true)
            .Replace("<pacs:Id><pacs:IBAN>GE95TB0000000123456789</pacs:IBAN></pacs:Id>", "<pacs:Id><pacs:Othr><pacs:Id>ACC-1</pacs:Id></pacs:Othr></pacs:Id>")
            .Replace("<pacs:Rsn><pacs:Cd>FOCR</pacs:Cd></pacs:Rsn>", "<pacs:Rsn><pacs:Prtry>CUSTOM</pacs:Prtry></pacs:Rsn>")
            .Replace("<pacs:AddtlInf>Returned in full</pacs:AddtlInf>", "<pacs:AddtlInf>First line</pacs:AddtlInf><pacs:AddtlInf>Second line</pacs:AddtlInf>")
            .Replace("<pacs:IntrBkSttlmDt>2026-10-04</pacs:IntrBkSttlmDt>", string.Empty);

        await ApplyAsync(core, await SignedAsync(core, unsigned), messageType: "pacs.004");

        var content = (await ReadOnlyAsync(core)).Pacs004;
        Assert.Equal("ACC-1", content.Debtor!.Account);
        Assert.Equal("CUSTOM", content.ReturnReasonCode);
        Assert.Equal("First line", content.AdditionalInformation);
        Assert.Null(content.ValueDate);
    }

    public static TheoryData<string> UnverifiableReturns() => new()
    {
        "definition", "signature", "schema", "two-returns", "not-ours", "return-id-differs", "no-return-id", "no-original-transaction", "no-instructed-bic", "malformed"
    };

    [Theory]
    [MemberData(nameof(UnverifiableReturns))]
    public async Task An_unverifiable_return_is_held_and_nothing_is_stored(string problem)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var xml = problem switch
        {
            "definition" => await SignedAsync(core, UnsignedReturn().Replace("<head:MsgDefIdr>pacs.004.001.13", "<head:MsgDefIdr>pacs.004.001.12")),
            "signature" => await SignedAsync(core, UnsignedReturn(), core.SigningCertificate),
            "schema" => await SignedAsync(core, UnsignedReturn().Replace("<pacs:ChrgBr>SLEV</pacs:ChrgBr>", "<pacs:ChrgBr>NOPE</pacs:ChrgBr>")),
            "two-returns" => await SignedAsync(core, UnsignedReturn(transactions: 2)),
            "not-ours" => await SignedAsync(core, UnsignedReturn(receiverBic: "TBCBGE22")),
            "return-id-differs" => await SignedAsync(core, UnsignedReturn(messageId: "OTHER-MSG")),
            "no-return-id" => await SignedAsync(core, UnsignedReturn().Replace("<pacs:RtrId>RTR-IN-1</pacs:RtrId>", string.Empty)),
            "no-original-transaction" => await SignedAsync(core, UnsignedReturn().Replace("<pacs:OrgnlTxId>ORIG-TX-1</pacs:OrgnlTxId>", string.Empty)),
            "no-instructed-bic" => await SignedAsync(core, UnsignedReturn().Replace("<pacs:DbtrAgt><pacs:FinInstnId><pacs:BICFI>BAGAGE22</pacs:BICFI>", "<pacs:DbtrAgt><pacs:FinInstnId><pacs:Nm>Bank</pacs:Nm>")),
            _ => "<Message>"
        };

        var (result, journalId) = await ApplyAsync(core, xml, messageType: "pacs.004");

        Assert.Equal(IncomingCompositionStatus.Held, result.Status);
        var receipt = await ReadReceiptAsync(core, journalId);
        Assert.Equal(InboundProcessingStatus.Held, receipt.Status);
        Assert.NotNull(receipt.HoldReason);
        await using var session = core.Database.Session();
        Assert.False(await session.Context.Set<IncomingTransferMetadata>().AnyAsync());
    }

    [Fact]
    public async Task Returns_are_identified_by_their_own_id_not_by_the_payment_they_return()
    {
        await using var core = await ProcessingHarness.CreateAsync();

        await ApplyAsync(core, await SignedAsync(core, UnsignedReturn(returnId: "RTR-IN-1")), sequence: 1, messageType: "pacs.004");
        var (second, _) = await ApplyAsync(core, await SignedAsync(core, UnsignedReturn(returnId: "RTR-IN-2")), sequence: 2, messageType: "pacs.004");
        var (again, _) = await ApplyAsync(core, await SignedAsync(core, UnsignedReturn(returnId: "RTR-IN-1")), sequence: 3, messageType: "pacs.004");
        var (changed, journalId) = await ApplyAsync(core, await SignedAsync(core, UnsignedReturn(returnId: "RTR-IN-1", amount: "50")), sequence: 4, messageType: "pacs.004");

        Assert.Equal(IncomingCompositionStatus.Terminal, second.Status);
        Assert.Equal(IncomingCompositionStatus.Terminal, again.Status);
        Assert.Equal(IncomingCompositionStatus.Held, changed.Status);
        Assert.Equal(IncomingTransferRegistration.ConflictReason, (await ReadReceiptAsync(core, journalId)).HoldReason);
        await using var session = core.Database.Session();
        Assert.Equal(["RTR-IN-1", "RTR-IN-2"], await session.Context.Set<IncomingTransfer>().Select(transfer => transfer.Key).OrderBy(key => key).ToListAsync());
    }

    [Fact]
    public async Task A_return_and_a_pacs009_with_the_same_key_are_two_transfers()
    {
        await using var core = await ProcessingHarness.CreateAsync();

        await ApplyAsync(core, await SignedAsync(core, Unsigned()), sequence: 1);
        await ApplyAsync(core, await SignedAsync(core, UnsignedReturn(returnId: "E2E-IN-1")), sequence: 2, messageType: "pacs.004");

        await using var session = core.Database.Session();
        Assert.Equal(["pacs.004", "pacs.009"], await session.Context.Set<IncomingTransfer>().Select(transfer => transfer.Kind).OrderBy(kind => kind).ToListAsync());
    }

    [Fact]
    public async Task A_return_is_submitted_under_its_return_id_and_only_that_id_is_checked_in_the_answer()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var cbs = new TransferCore { Submit = _ => Json("ACCP", "ANY-ORIGINAL-E2E", id: "OTHER-RETURN") };
        var id = await RegisterReturnAsync(core);

        await ProcessAsync(core, id, cbs);

        var refused = await ReadOnlyAsync(core);
        Assert.Equal(CoreOutcome.Unknown, refused.Transfer.CoreStatus);
        Assert.Equal([("submit", "RTR-IN-1")], cbs.Calls);

        cbs.Query = _ => Json("RJCT", "ANY-ORIGINAL-E2E", id: "RTR-IN-1", reason: "AC01");
        core.Clock.Now += new IncomingReconciliationOptions().RetryDelay(1);
        await ProcessAsync(core, id, cbs);

        var settled = await ReadOnlyAsync(core);
        Assert.Equal(CoreOutcome.Rejected, settled.Transfer.CoreStatus);
        Assert.Equal("AC01", settled.Transfer.CoreReasonCode);
        Assert.Equal([("submit", "RTR-IN-1"), ("query", "RTR-IN-1")], cbs.Calls);
    }

    [Fact]
    public async Task The_window_end_sends_an_unsettled_return_to_manual_review()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var cbs = new TransferCore { Submit = _ => Task.FromResult(new CoreResponse(503, "down")) };
        var id = await RegisterReturnAsync(core);
        await ProcessAsync(core, id, cbs);
        core.Clock.Now = ProcessingHarness.Start + new IncomingReconciliationOptions().Window;

        await ProcessAsync(core, id, cbs);

        var stored = await ReadOnlyAsync(core);
        Assert.NotNull(stored.Transfer.ManualReviewReason);
        Assert.Null(stored.NextActionAtUtc);
        Assert.Single(cbs.Calls);
    }

    [Fact]
    public async Task A_core_that_never_saw_the_return_gets_the_same_request_again()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var cbs = new TransferCore
        {
            Submit = _ => Task.FromResult(new CoreResponse(503, "down")),
            Query = _ => Task.FromResult(new CoreResponse(404, "unknown reference"))
        };
        var id = await RegisterReturnAsync(core);
        await ProcessAsync(core, id, cbs);
        core.Clock.Now += new IncomingReconciliationOptions().RetryDelay(1);
        await ProcessAsync(core, id, cbs);
        cbs.Submit = _ => Json("ACCP", "x", id: "RTR-IN-1");

        await ProcessAsync(core, id, cbs);

        Assert.Equal(CoreOutcome.Accepted, (await ReadOnlyAsync(core)).Transfer.CoreStatus);
        Assert.Equal(cbs.Transfers[0], cbs.Transfers[1]);
    }

    [Fact]
    public async Task Worker_acknowledges_hands_a_return_to_the_core_and_asks_for_its_status_by_return_kind()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var xml = await SignedAsync(core, UnsignedReturn());
        var deliveries = new ConcurrentQueue<(string Sequence, string Body)>([("9", xml)]);
        var acknowledgements = new ConcurrentQueue<string?>();
        var submissions = new ConcurrentQueue<(string? Key, string Body)>();
        var queries = new ConcurrentQueue<string>();
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            if (context.Request.Method == "GET" && context.Request.Path == "/Message")
            {
                if (deliveries.TryDequeue(out var delivery))
                {
                    context.Response.Headers["X-MONTRAN-IPS-MessageSeq"] = delivery.Sequence;
                    context.Response.Headers["X-MONTRAN-IPS-MessageType"] = "pacs.004";
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
            }
            else if (context.Request.Method == "POST" && context.Request.Path == "/api/ips/pacs004/receive")
            {
                submissions.Enqueue((context.Request.Headers["Idempotency-Key"], await new StreamReader(context.Request.Body).ReadToEndAsync()));
                context.Response.StatusCode = 503;
            }
            else if (context.Request.Method == "GET" && context.Request.Path == "/api/ips/payments/status")
            {
                queries.Enqueue(context.Request.QueryString.Value ?? string.Empty);
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"status\":\"ACCP\",\"id\":\"RTR-IN-1\"}");
            }
            else
            {
                context.Response.StatusCode = 404;
            }
        });
        using var certificates = new TransportCertificates();
        using var host = Host(core, server.Url, certificates, new IncomingReconciliationOptions(retryDelays: [TimeSpan.FromMilliseconds(50)]));
        await host.StartAsync();
        try
        {
            await EventuallyAsync(async () => await HasAcceptedTransferAsync(core));
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Equal(["9"], acknowledgements);
        var submission = Assert.Single(submissions);
        Assert.Equal("RTR-IN-1", submission.Key);
        Assert.Contains("\"Id\":\"RTR-IN-1\"", submission.Body);
        Assert.Contains("\"TransactionId\":\"ORIG-TX-1\"", submission.Body);
        Assert.Equal("?messageKind=Pacs004&reference=RTR-IN-1", queries.First());
    }

    [Fact]
    public async Task Worker_acknowledges_recalls_and_cancellations_after_storing_them_and_leaves_other_unsupported_types_unacknowledged()
    {
        using var probe = new MetricsProbe();
        await using var core = await ProcessingHarness.CreateAsync();
        // The unsupported type comes first, so an acknowledgement of it would show before the recalls'. The first
        // acknowledgement fails and sequence 11 is delivered again, which must be acknowledged again and stored once.
        var deliveries = new ConcurrentQueue<(string Sequence, string Type)>([("13", "camt.053"), ("11", "camt.056"), ("11", "camt.056"), ("12", "camt.029"), ("14", "camt.055")]);
        var acknowledgements = new ConcurrentQueue<string?>();
        var acknowledgementCalls = 0;
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            if (context.Request.Method == "GET" && context.Request.Path == "/Message")
            {
                if (deliveries.TryDequeue(out var delivery))
                {
                    context.Response.Headers["X-MONTRAN-IPS-MessageSeq"] = delivery.Sequence;
                    context.Response.Headers["X-MONTRAN-IPS-MessageType"] = delivery.Type;
                    await context.Response.WriteAsync("<" + delivery.Type + " />");
                }
                else
                {
                    context.Response.Headers["X-MONTRAN-IPS-ReqSts"] = "EMPTY";
                }
            }
            else if (context.Request.Method == "POST" && context.Request.Path == "/MessageAck")
            {
                // Acknowledgements run concurrently since 012a, so the failing one is the first to arrive, counted atomically.
                acknowledgements.Enqueue(context.Request.Headers["X-MONTRAN-IPS-MessageSeq"]);
                context.Response.StatusCode = Interlocked.Increment(ref acknowledgementCalls) == 1 ? 500 : 200;
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
            await EventuallyAsync(async () =>
            {
                await using var session = core.Database.Session();
                var statuses = await session.Context.Set<InboundJournalEntry>().OrderBy(entry => entry.Sequence).Select(entry => entry.Status).ToListAsync();
                return statuses.SequenceEqual([InboundProcessingStatus.Processed, InboundProcessingStatus.Processed, InboundProcessingStatus.Held, InboundProcessingStatus.Processed]);
            });
            await EventuallyAsync(() => Task.FromResult(acknowledgements.Count >= 4));
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Equal(["11", "11", "12", "14"], acknowledgements.Order());
        // Five messages arrived, one of them a redelivery; the first acknowledgement failed and the other three succeeded.
        Assert.Equal(1, probe.Of("ips.incoming.receipts").Count(measured => (bool)measured.Tags["redelivery"]!));
        Assert.Equal(
            new Dictionary<string, long> { ["camt.029"] = 1, ["camt.055"] = 1, ["camt.056"] = 2, ["other"] = 1 },
            probe.CountBy("ips.incoming.receipts", "message_type"));
        Assert.Equal(new Dictionary<string, long> { ["http_error"] = 1, ["ok"] = 3 }, probe.CountBy("ips.incoming.acknowledgements", "result"));
        await using var check = core.Database.Session();
        Assert.Equal(1, await check.Context.Set<InboundJournalEntry>().Where(entry => entry.Sequence == 11).Select(entry => entry.DuplicateCount).SingleAsync());
    }

    [Fact]
    public async Task A_verified_initiation_is_stored_with_the_content_the_core_will_receive()
    {
        await using var core = await ProcessingHarness.CreateAsync();

        var (result, journalId) = await ApplyAsync(core, await SignedAsync(core, UnsignedInitiation(full: true)), messageType: "pain.001");

        Assert.Equal(IncomingCompositionStatus.Terminal, result.Status);
        Assert.Equal(InboundProcessingStatus.Processed, (await ReadReceiptAsync(core, journalId)).Status);
        var stored = await ReadOnlyAsync(core);
        Assert.Equal(("pain.001", "PMTINF-1"), (stored.Transfer.Kind, stored.Transfer.Key));
        Assert.Equal(["incoming-transfer.registered"], stored.Events);
        var initiation = stored.Pain001;
        Assert.Equal(("PAIN001-MSG-1", "PISP LLC", "PISPGE22"), (initiation.MessageId, initiation.InitiatingParty!.Name, initiation.InitiatingParty.Bic));
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 14, 5, 7, 123, TimeSpan.FromHours(4)), initiation.CreatedAt);
        Assert.Equal(TimeSpan.FromHours(4), initiation.CreatedAt.Offset);
        Assert.Equal(("INST", "INST", "OTHR", new DateOnly(2026, 10, 1)), (initiation.ServiceLevelCode, initiation.LocalInstrumentCode, initiation.CategoryPurposeCode, initiation.RequestedExecutionDate));
        Assert.Equal((1, "Giorgi Beridze", "01001012345", "BAGAGE22", "GE95TB0000000123456789"),
            (initiation.Debtor.Type, initiation.Debtor.Name, initiation.Debtor.Identifier, initiation.Debtor.ParticipantBic, initiation.Debtor.Account));
        Assert.Equal("Tbilisi", initiation.Debtor.Address!.TownName);
        Assert.Equal((0, "400000002", "TBCBGE22", "GE29NB0000000101904917"),
            (initiation.Creditor!.Type, initiation.Creditor.Identifier, initiation.Creditor.ParticipantBic, initiation.Creditor.Account));
        Assert.Equal(("INS-1", "PISP-E2E-1", 25.50m, "GEL", "GDDS"), (initiation.InstructionId, initiation.EndToEndId, initiation.Amount, initiation.Currency, initiation.PurposeCode));
        Assert.Equal("Invoice 77", initiation.Remittance!.Unstructured);
        var structured = Assert.Single(initiation.Remittance.Structured!);
        Assert.Equal(("SCOR", "ORDER-77"), (structured.ReferenceType, structured.Reference));
        Assert.Equal("Ultimate Payer", initiation.UltimateDebtor!.Name);
        Assert.Equal(core.Clock.Now + new IncomingReconciliationOptions().Window, stored.DeadlineUtc);
    }

    [Fact]
    public async Task A_minimal_initiation_leaves_the_optional_parts_absent_and_takes_a_date_time_execution_date()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var unsigned = UnsignedInitiation()
            .Replace("<ReqdExctnDt><Dt>2026-10-01</Dt></ReqdExctnDt>", "<ReqdExctnDt><DtTm>2026-10-02T09:00:00+04:00</DtTm></ReqdExctnDt>")
            .Replace("<IBAN>GE29NB0000000101904917</IBAN>", "<Othr><Id>ACC-77</Id></Othr>");

        await ApplyAsync(core, await SignedAsync(core, unsigned), messageType: "pain.001");

        var initiation = (await ReadOnlyAsync(core)).Pain001;
        Assert.Equal(new DateOnly(2026, 10, 2), initiation.RequestedExecutionDate);
        Assert.Equal("ACC-77", initiation.Creditor!.Account);
        Assert.Null(initiation.Remittance);
        Assert.Null(initiation.UltimateDebtor);
        Assert.Null(initiation.UltimateCreditor);
        Assert.Null(initiation.PurposeCode);
    }

    // The schema makes the message id, the initiation id and the creation time mandatory, so those are held as malformed.
    public static TheoryData<string, string> UnverifiableInitiations() => new()
    {
        { "definition", "Unsupported message definition." },
        { "signature", "Untrusted message signature." },
        { "schema", "Malformed or unsupported initiation content." },
        { "two-payments", "exactly one payment instruction" },
        { "no-initiation-id", "Malformed or unsupported initiation content." },
        { "too-long-id", "longer than 31 characters" },
        { "no-message-id", "Malformed or unsupported initiation content." },
        { "no-creation-time", "Malformed or unsupported initiation content." },
        { "out-of-range-creation-time", "Malformed or unsupported initiation content." },
        { "no-debtor-bic", "no BICFI" },
        { "not-ours", "not addressed to our participant" },
        { "malformed", "Malformed or unsupported initiation content." }
    };

    [Theory]
    [MemberData(nameof(UnverifiableInitiations))]
    public async Task An_unverifiable_initiation_is_held_for_its_own_reason_and_nothing_is_stored(string problem, string reason)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var xml = problem switch
        {
            "definition" => await SignedAsync(core, UnsignedInitiation().Replace("<head:MsgDefIdr>pain.001.001.12", "<head:MsgDefIdr>pain.001.001.11")),
            "signature" => await SignedAsync(core, UnsignedInitiation(), core.SigningCertificate),
            "schema" => await SignedAsync(core, UnsignedInitiation().Replace("<PmtMtd>TRF</PmtMtd>", "<PmtMtd>XXX</PmtMtd>")),
            "two-payments" => await SignedAsync(core, UnsignedInitiation(payments: 2)),
            "no-initiation-id" => await SignedAsync(core, UnsignedInitiation().Replace("<PmtInfId>PMTINF-1</PmtInfId>", string.Empty)),
            "too-long-id" => await SignedAsync(core, UnsignedInitiation(paymentInformationId: new string('P', 32))),
            "no-message-id" => await SignedAsync(core, UnsignedInitiation().Replace("<MsgId>PAIN001-MSG-1</MsgId>", string.Empty)),
            "no-creation-time" => await SignedAsync(core, UnsignedInitiation().Replace("<CreDtTm>2026-10-01T14:05:07.123+04:00</CreDtTm>", string.Empty)),
            "out-of-range-creation-time" => await SignedAsync(core, UnsignedInitiation().Replace("<CreDtTm>2026-10-01T14:05:07.123+04:00</CreDtTm>", "<CreDtTm>0001-01-01T00:00:00+04:00</CreDtTm>")),
            "no-debtor-bic" => await SignedAsync(core, UnsignedInitiation().Replace("<DbtrAgt><FinInstnId><BICFI>BAGAGE22</BICFI>", "<DbtrAgt><FinInstnId><Nm>Bank</Nm>")),
            "not-ours" => await SignedAsync(core, UnsignedInitiation(debtorBic: "TBCBGE22")),
            _ => "<Message>"
        };

        var (result, journalId) = await ApplyAsync(core, xml, messageType: "pain.001");

        Assert.Equal(IncomingCompositionStatus.Held, result.Status);
        var receipt = await ReadReceiptAsync(core, journalId);
        Assert.Equal(InboundProcessingStatus.Held, receipt.Status);
        Assert.Contains(reason, receipt.HoldReason);
        await using var session = core.Database.Session();
        Assert.False(await session.Context.Set<IncomingTransferMetadata>().AnyAsync());
    }

    [Fact]
    public async Task The_definition_spelled_as_in_the_registry_is_accepted()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var unsigned = UnsignedInitiation().Replace("<head:MsgDefIdr>pain.001.001.12</head:MsgDefIdr>", "<head:MsgDefIdr>pain.001.001.012</head:MsgDefIdr>");

        var (result, _) = await ApplyAsync(core, await SignedAsync(core, unsigned), messageType: "pain.001.001.012");

        Assert.Equal(IncomingCompositionStatus.Terminal, result.Status);
        Assert.Equal("PMTINF-1", (await ReadOnlyAsync(core)).Transfer.Key);
    }

    [Fact]
    public async Task An_execution_date_with_a_time_zone_is_left_out_and_the_initiation_is_still_stored()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var unsigned = UnsignedInitiation().Replace("<ReqdExctnDt><Dt>2026-10-01</Dt></ReqdExctnDt>", "<ReqdExctnDt><Dt>2026-10-01+04:00</Dt></ReqdExctnDt>");

        var (result, _) = await ApplyAsync(core, await SignedAsync(core, unsigned), messageType: "pain.001");

        Assert.Equal(IncomingCompositionStatus.Terminal, result.Status);
        Assert.Null((await ReadOnlyAsync(core)).Pain001.RequestedExecutionDate);
    }

    [Fact]
    public async Task An_initiation_id_of_31_characters_is_accepted_and_32_is_held_for_its_length()
    {
        await using var core = await ProcessingHarness.CreateAsync();

        var (accepted, _) = await ApplyAsync(core, await SignedAsync(core, UnsignedInitiation(paymentInformationId: new string('P', 31))), sequence: 1, messageType: "pain.001");
        var (held, journalId) = await ApplyAsync(core, await SignedAsync(core, UnsignedInitiation(paymentInformationId: new string('Q', 32))), sequence: 2, messageType: "pain.001");

        Assert.Equal(IncomingCompositionStatus.Terminal, accepted.Status);
        Assert.Equal(IncomingCompositionStatus.Held, held.Status);
        Assert.Contains("31", (await ReadReceiptAsync(core, journalId)).HoldReason);
    }

    [Fact]
    public async Task A_redelivered_initiation_with_structured_remittance_is_the_same_transfer_and_changed_content_is_held()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var xml = await SignedAsync(core, UnsignedInitiation(full: true));
        await ApplyAsync(core, xml, sequence: 1, messageType: "pain.001");

        var (again, _) = await ApplyAsync(core, xml, sequence: 2, messageType: "pain.001");
        var (changed, journalId) = await ApplyAsync(core, await SignedAsync(core, UnsignedInitiation(full: true, amount: "30.00")), sequence: 3, messageType: "pain.001");

        Assert.Equal(IncomingCompositionStatus.Terminal, again.Status);
        Assert.Equal(IncomingCompositionStatus.Held, changed.Status);
        Assert.Equal(IncomingTransferRegistration.ConflictReason, (await ReadReceiptAsync(core, journalId)).HoldReason);
        await using var session = core.Database.Session();
        Assert.Single(await session.Context.Set<IncomingTransfer>().ToListAsync());
    }

    [Fact]
    public async Task An_initiation_and_a_pacs009_with_the_same_key_are_two_transfers()
    {
        await using var core = await ProcessingHarness.CreateAsync();

        await ApplyAsync(core, await SignedAsync(core, UnsignedInitiation()), sequence: 1, messageType: "pain.001");
        await ApplyAsync(core, await SignedAsync(core, Unsigned().Replace("E2E-IN-1", "PMTINF-1")), sequence: 2);

        await using var session = core.Database.Session();
        Assert.Equal(["pacs.009", "pain.001"], await session.Context.Set<IncomingTransfer>().OrderBy(transfer => transfer.Kind).Select(transfer => transfer.Kind).ToListAsync());
    }

    [Fact]
    public async Task An_initiation_is_submitted_under_its_initiation_id_and_only_that_id_is_checked_in_the_answer()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        await ApplyAsync(core, await SignedAsync(core, UnsignedInitiation()), messageType: "pain.001");
        var id = (await ReadOnlyAsync(core)).Transfer.Id;
        var cbs = new TransferCore { Submit = _ => Json("ACCP", "PISP-E2E-1", id: "OTHER-INITIATION") };

        await ProcessAsync(core, id, cbs);

        Assert.Equal(CoreOutcome.Unknown, (await ReadOnlyAsync(core)).Transfer.CoreStatus);
        Assert.Equal([("submit", "PMTINF-1")], cbs.Calls);

        cbs.Query = _ => Json("ACCP", "ANY-E2E", id: "PMTINF-1");
        core.Clock.Now += new IncomingReconciliationOptions().RetryDelay(1);
        await ProcessAsync(core, id, cbs);

        Assert.Equal(CoreOutcome.Accepted, (await ReadOnlyAsync(core)).Transfer.CoreStatus);
        Assert.Equal([("submit", "PMTINF-1"), ("query", "PMTINF-1")], cbs.Calls);
    }

    [Fact]
    public async Task Worker_acknowledges_hands_an_initiation_to_the_core_and_asks_for_its_status_by_initiation_kind()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var xml = await SignedAsync(core, UnsignedInitiation(full: true));
        var deliveries = new ConcurrentQueue<(string Sequence, string Body)>([("21", xml)]);
        var acknowledgements = new ConcurrentQueue<string?>();
        var submissions = new ConcurrentQueue<(string? Key, string Body)>();
        var queries = new ConcurrentQueue<string>();
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            if (context.Request.Method == "GET" && context.Request.Path == "/Message")
            {
                if (deliveries.TryDequeue(out var delivery))
                {
                    context.Response.Headers["X-MONTRAN-IPS-MessageSeq"] = delivery.Sequence;
                    context.Response.Headers["X-MONTRAN-IPS-MessageType"] = "pain.001";
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
            }
            else if (context.Request.Method == "POST" && context.Request.Path == "/api/ips/pain001/receive")
            {
                submissions.Enqueue((context.Request.Headers["Idempotency-Key"], await new StreamReader(context.Request.Body).ReadToEndAsync()));
                context.Response.StatusCode = 503;
            }
            else if (context.Request.Method == "GET" && context.Request.Path == "/api/ips/payments/status")
            {
                queries.Enqueue(context.Request.QueryString.Value ?? string.Empty);
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"status\":\"ACCP\",\"id\":\"PMTINF-1\"}");
            }
            else
            {
                context.Response.StatusCode = 404;
            }
        });
        using var certificates = new TransportCertificates();
        using var host = Host(core, server.Url, certificates, new IncomingReconciliationOptions(retryDelays: [TimeSpan.FromMilliseconds(50)]));
        await host.StartAsync();
        try
        {
            await EventuallyAsync(async () => await HasAcceptedTransferAsync(core));
        }
        finally
        {
            await host.StopAsync();
        }

        Assert.Equal(["21"], acknowledgements);
        var submission = Assert.Single(submissions);
        Assert.Equal("PMTINF-1", submission.Key);
        Assert.Contains("\"paymentInformationId\":\"PMTINF-1\"", submission.Body);
        Assert.Contains("\"endToEndId\":\"PISP-E2E-1\"", submission.Body);
        Assert.Equal("?messageKind=Pain001&reference=PMTINF-1", queries.First());
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

    private static string UnsignedReturn(
        bool full = false,
        string receiverBic = Participant,
        int transactions = 1,
        string amount = "100.5",
        string returnId = "RTR-IN-1",
        string? messageId = null)
    {
        const string head = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";
        const string pacs = "urn:iso:std:iso:20022:tech:xsd:pacs.004.001.13";
        static string Agent(string bic, string? member = null) =>
            $"<pacs:FinInstnId><pacs:BICFI>{bic}</pacs:BICFI>" +
            (member is null ? string.Empty : $"<pacs:ClrSysMmbId><pacs:ClrSysId><pacs:Cd>GE</pacs:Cd></pacs:ClrSysId><pacs:MmbId>{member}</pacs:MmbId></pacs:ClrSysMmbId>") +
            "</pacs:FinInstnId>";
        static string Party(string name, string? identification) =>
            $"<pacs:Pty><pacs:Nm>{name}</pacs:Nm>{identification}</pacs:Pty>";
        static string Account(string iban) => $"<pacs:Id><pacs:IBAN>{iban}</pacs:IBAN></pacs:Id>";
        var original = full ? "<pacs:OrgnlGrpInf><pacs:OrgnlMsgId>M-ORIG</pacs:OrgnlMsgId><pacs:OrgnlMsgNmId>pacs.008.001.12</pacs:OrgnlMsgNmId><pacs:OrgnlCreDtTm>2026-10-03T10:00:00Z</pacs:OrgnlCreDtTm></pacs:OrgnlGrpInf><pacs:OrgnlInstrId>ORIG-INSTR-1</pacs:OrgnlInstrId>" : string.Empty;
        var uetr = full ? "<pacs:OrgnlUETR>0f3c9c1e-9d1a-4a43-8f8e-2b1d0a1d5a11</pacs:OrgnlUETR>" : string.Empty;
        var additional = full ? "<pacs:AddtlInf>Returned in full</pacs:AddtlInf>" : string.Empty;
        var debtorId = full ? "<pacs:Id><pacs:OrgId><pacs:Othr><pacs:Id>123456789</pacs:Id></pacs:Othr></pacs:OrgId></pacs:Id>" : null;
        var creditorId = full ? "<pacs:Id><pacs:PrvtId><pacs:Othr><pacs:Id>01001</pacs:Id></pacs:Othr></pacs:PrvtId></pacs:Id>" : null;
        var transaction =
            $"<pacs:TxInf><pacs:RtrId>{returnId}</pacs:RtrId>{original}<pacs:OrgnlEndToEndId>ORIG-E2E-1</pacs:OrgnlEndToEndId><pacs:OrgnlTxId>ORIG-TX-1</pacs:OrgnlTxId>{uetr}" +
            $"<pacs:OrgnlIntrBkSttlmAmt Ccy=\"GEL\">100.5</pacs:OrgnlIntrBkSttlmAmt><pacs:RtrdIntrBkSttlmAmt Ccy=\"GEL\">{amount}</pacs:RtrdIntrBkSttlmAmt><pacs:ChrgBr>SLEV</pacs:ChrgBr>" +
            "<pacs:RtrRsnInf><pacs:Orgtr><pacs:Id><pacs:OrgId><pacs:AnyBIC>TBCBGE22</pacs:AnyBIC></pacs:OrgId></pacs:Id></pacs:Orgtr><pacs:Rsn><pacs:Cd>FOCR</pacs:Cd></pacs:Rsn>" +
            $"{additional}</pacs:RtrRsnInf>" +
            "<pacs:OrgnlTxRef><pacs:IntrBkSttlmDt>2026-10-03</pacs:IntrBkSttlmDt><pacs:PmtTpInf><pacs:SvcLvl><pacs:Cd>INST</pacs:Cd></pacs:SvcLvl><pacs:LclInstrm><pacs:Cd>INST</pacs:Cd></pacs:LclInstrm></pacs:PmtTpInf>" +
            $"<pacs:Dbtr>{Party("Original Payer", debtorId)}</pacs:Dbtr><pacs:DbtrAcct>{Account("GE95TB0000000123456789")}</pacs:DbtrAcct><pacs:DbtrAgt>{Agent(receiverBic)}</pacs:DbtrAgt>" +
            $"<pacs:CdtrAgt>{Agent("TBCBGE22", full ? "MEMBER-9" : null)}</pacs:CdtrAgt><pacs:Cdtr>{Party("Original Payee", creditorId)}</pacs:Cdtr><pacs:CdtrAcct>{Account("GE29NB0000000101904917")}</pacs:CdtrAcct></pacs:OrgnlTxRef></pacs:TxInf>";
        return
            $"<Message xmlns:head=\"{head}\" xmlns:pacs=\"{pacs}\"><head:AppHdr>" +
            "<head:Fr><head:FIId><head:FinInstnId><head:BICFI>NBGEGE22</head:BICFI></head:FinInstnId></head:FIId></head:Fr>" +
            $"<head:To><head:FIId><head:FinInstnId><head:BICFI>{Participant}</head:BICFI></head:FinInstnId></head:FIId></head:To>" +
            $"<head:BizMsgIdr>{returnId}</head:BizMsgIdr><head:MsgDefIdr>pacs.004.001.13</head:MsgDefIdr><head:CreDt>2026-10-04T10:00:00.0000000Z</head:CreDt><head:Sgntr/></head:AppHdr>" +
            $"<pacs:Document><pacs:PmtRtr><pacs:GrpHdr><pacs:MsgId>{messageId ?? returnId}</pacs:MsgId><pacs:CreDtTm>2026-10-04T10:00:00.0000000Z</pacs:CreDtTm>" +
            $"<pacs:NbOfTxs>{transactions}</pacs:NbOfTxs><pacs:TtlRtrdIntrBkSttlmAmt Ccy=\"GEL\">{amount}</pacs:TtlRtrdIntrBkSttlmAmt><pacs:IntrBkSttlmDt>2026-10-04</pacs:IntrBkSttlmDt>" +
            "<pacs:SttlmInf><pacs:SttlmMtd>CLRG</pacs:SttlmMtd><pacs:ClrSys><pacs:Cd>IPS</pacs:Cd></pacs:ClrSys></pacs:SttlmInf>" +
            "<pacs:InstgAgt><pacs:FinInstnId><pacs:BICFI>TBCBGE22</pacs:BICFI></pacs:FinInstnId></pacs:InstgAgt></pacs:GrpHdr>" +
            string.Concat(Enumerable.Repeat(transaction, transactions)) +
            "</pacs:PmtRtr></pacs:Document></Message>";
    }

    private static string UnsignedInitiation(
        bool full = false,
        string paymentInformationId = "PMTINF-1",
        string debtorBic = Participant,
        int payments = 1,
        string amount = "25.50")
    {
        const string head = "urn:iso:std:iso:20022:tech:xsd:head.001.001.03";
        var ultimateDebtor = full ? "<UltmtDbtr><Nm>Ultimate Payer</Nm><Id><OrgId><Othr><Id>111222333</Id></Othr></OrgId></Id></UltmtDbtr>" : string.Empty;
        var categoryPurpose = full ? "<CtgyPurp><Cd>OTHR</Cd></CtgyPurp>" : string.Empty;
        var purpose = full ? "<Purp><Cd>GDDS</Cd></Purp>" : string.Empty;
        var remittance = full
            ? "<RmtInf><Ustrd>Invoice 77</Ustrd><Strd><CdtrRefInf><Tp><CdOrPrtry><Cd>SCOR</Cd></CdOrPrtry><Issr>PISP</Issr></Tp><Ref>ORDER-77</Ref></CdtrRefInf></Strd></RmtInf>"
            : string.Empty;
        var payment =
            $"<PmtInf><PmtInfId>{paymentInformationId}</PmtInfId><PmtMtd>TRF</PmtMtd>" +
            $"<PmtTpInf><SvcLvl><Cd>INST</Cd></SvcLvl><LclInstrm><Cd>INST</Cd></LclInstrm>{categoryPurpose}</PmtTpInf>" +
            "<ReqdExctnDt><Dt>2026-10-01</Dt></ReqdExctnDt>" +
            "<Dbtr><Nm>Giorgi Beridze</Nm><PstlAdr><TwnNm>Tbilisi</TwnNm><Ctry>GE</Ctry></PstlAdr><Id><PrvtId><Othr><Id>01001012345</Id></Othr></PrvtId></Id></Dbtr>" +
            $"<DbtrAcct><Id><IBAN>GE95TB0000000123456789</IBAN></Id></DbtrAcct><DbtrAgt><FinInstnId><BICFI>{debtorBic}</BICFI></FinInstnId></DbtrAgt>{ultimateDebtor}" +
            "<ChrgBr>SLEV</ChrgBr><CdtTrfTxInf>" +
            "<PmtId><InstrId>INS-1</InstrId><EndToEndId>PISP-E2E-1</EndToEndId></PmtId>" +
            $"<Amt><InstdAmt Ccy=\"GEL\">{amount}</InstdAmt></Amt>" +
            "<CdtrAgt><FinInstnId><BICFI>TBCBGE22</BICFI></FinInstnId></CdtrAgt>" +
            "<Cdtr><Nm>Receiver LLC</Nm><Id><OrgId><Othr><Id>400000002</Id></Othr></OrgId></Id></Cdtr>" +
            $"<CdtrAcct><Id><IBAN>GE29NB0000000101904917</IBAN></Id></CdtrAcct>{purpose}{remittance}</CdtTrfTxInf></PmtInf>";
        return
            $"<Message xmlns:head=\"{head}\"><head:AppHdr>" +
            "<head:Fr><head:FIId><head:FinInstnId><head:BICFI>NETCMNUB</head:BICFI></head:FinInstnId></head:FIId></head:Fr>" +
            $"<head:To><head:FIId><head:FinInstnId><head:BICFI>{Participant}</head:BICFI></head:FinInstnId></head:FIId></head:To>" +
            "<head:BizMsgIdr>PAIN001-MSG-1</head:BizMsgIdr><head:MsgDefIdr>pain.001.001.12</head:MsgDefIdr><head:CreDt>2026-10-01T10:05:07.123Z</head:CreDt><head:Sgntr/></head:AppHdr>" +
            "<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pain.001.001.12\"><CstmrCdtTrfInitn><GrpHdr><MsgId>PAIN001-MSG-1</MsgId>" +
            $"<CreDtTm>2026-10-01T14:05:07.123+04:00</CreDtTm><NbOfTxs>1</NbOfTxs><CtrlSum>{amount}</CtrlSum>" +
            "<InitgPty><Nm>PISP LLC</Nm><Id><OrgId><AnyBIC>PISPGE22</AnyBIC></OrgId></Id></InitgPty></GrpHdr>" +
            string.Concat(Enumerable.Repeat(payment, payments)) +
            "</CstmrCdtTrfInitn></Document></Message>";
    }

    private static async Task<string> SignedAsync(ProcessingHarness core, string unsigned, X509Certificate2? signer = null) =>
        (await IpsReplies.SignAsync(signer ?? core.IpsCertificate, unsigned))[0];

    private static async Task<(IncomingCompositionResult Result, Guid JournalId)> ApplyAsync(
        ProcessingHarness core,
        string xml,
        long sequence = 1,
        string messageType = "pacs.009",
        DateTimeOffset? receivedAt = null)
    {
        Guid journalId;
        await using (var registering = core.Database.Session())
        {
            var registration = await new InboundReceiptRepository(registering.Context)
                .StageRegistrationAsync(new(Participant, sequence, messageType, xml, false, receivedAt ?? core.Clock.Now), default);
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
        var trust = new IpsSignatureTrust([core.IpsCertificate], core.Clock);
        var handler = new IncomingTransferRegistration(
            [new IncomingPacs009Protocol(trust), new IncomingPacs004Protocol(trust), new IncomingPain001Protocol(trust)],
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

    private static async Task<Guid> RegisterReturnAsync(ProcessingHarness core)
    {
        await ApplyAsync(core, await SignedAsync(core, UnsignedReturn()), messageType: "pacs.004");
        return (await ReadOnlyAsync(core, "pacs.004")).Transfer.Id;
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

    private static async Task<StoredTransfer> ReadOnlyAsync(ProcessingHarness core, string? kind = null)
    {
        await using var session = core.Database.Session();
        var metadata = await session.Context.Set<IncomingTransferMetadata>().Include(x => x.Transfer)
            .Where(x => kind == null || x.Transfer.Kind == kind).SingleAsync();
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
        return await session.Context.Set<IncomingTransfer>().AnyAsync(transfer => transfer.CoreStatus == CoreOutcome.Accepted);
    }

    private static async Task<StoredInboundReceipt> ReadReceiptAsync(ProcessingHarness core, Guid journalId)
    {
        await using var session = core.Database.Session();
        return (await new InboundReceiptRepository(session.Context).ReadAsync(journalId, default))!;
    }

    private static Task<CoreResponse> Accept() => Json("ACCP", "E2E-IN-1");

    private static Task<CoreResponse> Json(string status, string endToEndId, string? reason = null, string? id = null) =>
        Task.FromResult(new CoreResponse(200, $"{{\"status\":\"{status}\",\"endToEndId\":\"{endToEndId}\"" +
            (id is null ? string.Empty : $",\"id\":\"{id}\"") + (reason is null ? string.Empty : $",\"reasonCode\":\"{reason}\"") + "}"));

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

    private static IHost Host(ProcessingHarness core, string url, TransportCertificates certificates, IncomingReconciliationOptions? reconciliation = null)
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
            Ips = new() { BaseUrl = url, ConnectionLimit = 5 },
            Cbs = new() { BaseUrl = url, ConnectionLimit = 4 },
            SigningCertificate = certificates.Identity,
            IpsSignatureTrust = [certificates.SavePublic(core.IpsCertificate, "ips.pem")]
        });
        builder.Services.AddSingleton(new IncomingWorkerOptions { Enabled = true, EmptyDelay = TimeSpan.FromMilliseconds(20), ErrorDelay = TimeSpan.FromMilliseconds(20) });
        builder.Services.AddSingleton(new InboundSchedulingOptions(capacity: 2, discoveryBatch: 2, discoveryInterval: TimeSpan.FromMilliseconds(20)));
        builder.Services.AddSingleton(new Pacs008ProtocolProfile("NBGEGE22"));
        builder.Services.AddSingleton(new Pacs008SigningPolicy(false, false));
        builder.Services.AddSingleton<Pacs008MessageSigner>();
        builder.Services.AddSingleton(reconciliation ?? new IncomingReconciliationOptions());
        builder.Services.AddIncomingHttpClients();
        builder.Services.AddIncomingWorkers();
        builder.Services.Configure<HostOptions>(options => options.ServicesStopConcurrently = true);
        return builder.Build();
    }

    private sealed record StoredTransfer(
        IncomingTransfer Transfer,
        IIncomingTransferContent Content,
        DateTimeOffset DeadlineUtc,
        DateTimeOffset? NextActionAtUtc,
        Guid? ClaimToken,
        IReadOnlyList<string> Events)
    {
        public IncomingPacs009 Pacs009 => (IncomingPacs009)Content;

        public IncomingPacs004 Pacs004 => (IncomingPacs004)Content;

        public IncomingPain001 Pain001 => (IncomingPain001)Content;
    }

    // The core system: records each call and answers as scripted; by default every submission is accepted.
    private sealed class TransferCore : IIncomingTransferCoreClient
    {
        private readonly object _gate = new();
        private readonly List<(string Kind, string EndToEndId)> _calls = [];
        private readonly List<IIncomingTransferContent> _transfers = [];

        public Func<IIncomingTransferContent, Task<CoreResponse>> Submit { get; set; } = _ => Accept();
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

        public IReadOnlyList<IIncomingTransferContent> Transfers
        {
            get
            {
                lock (_gate)
                {
                    return _transfers.ToArray();
                }
            }
        }

        Task<CoreResponse> IIncomingTransferCoreClient.SubmitAsync(string participantBic, IIncomingTransferContent transfer, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _calls.Add(("submit", transfer.Key));
                _transfers.Add(transfer);
            }

            return Submit(transfer);
        }

        Task<CoreResponse> IIncomingTransferCoreClient.QueryAsync(string participantBic, string kind, string key, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _calls.Add(("query", key));
            }

            return Query(key);
        }
    }

    // Fails the save that would record the core's final answer, after the call was made.
    private sealed class CrashWhenOutcomeIsSaved : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            eventData.Context!.ChangeTracker.Entries<IncomingTransfer>().Any(entry => entry.Entity.CoreStatus == CoreOutcome.Accepted)
                ? throw new SimulatedCrash()
                : ValueTask.FromResult(result);
    }
}
