using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using IPS.Middleware.Application.Inbound.Composition;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Transfers;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Payments.Recalls;
using IPS.Middleware.Domain.Inbound;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Inbound;
using IPS.Middleware.Infrastructure.Inbound.Pacs008;
using IPS.Middleware.Infrastructure.Inbound.Transfers;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Inbound.Workers;
using IPS.Middleware.Infrastructure.Payments.Pacs008;
using IPS.Middleware.Infrastructure.Payments.Pacs008.Signing;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Events;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Repositories.Inbound;
using IPS.Middleware.Infrastructure.Repositories.Payments;
using IPS.Middleware.IntegrationTests.Payments;
using IPS.Middleware.IntegrationTests.Transport;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

// 012c: an incoming camt.056 recall request and camt.055 cancellation request are verified, stored once and delivered to
// the core system on the incoming transfer engine; a camt.029 refusal is delivered only when it matches our recall, which
// records the refusal in the same commit. Real SQL and independently Java-signed IPS fixtures; the core is a simulator.
[Collection("Metrics")]
public sealed class IncomingRecallTests
{
    private const string Participant = "BAGAGE22";
    private const string Other = "TBCBGE22";
    private const string Recall = "camt.056";
    private const string Cancellation = "camt.055";
    private const string Refusal = "camt.029";

    // The recall our core sent (Camt056Fixture): its message id and the payment it recalls.
    private const string OurRecallId = "RCL-processing";
    private const string RecalledEndToEndId = "ORIG-E2E-1";
    private const string RecalledTransactionId = "ORIG-TX-1";

    [Fact]
    public async Task A_verified_recall_request_is_stored_once_with_the_content_the_core_will_receive()
    {
        await using var core = await ProcessingHarness.CreateAsync();

        var (result, journalId) = await ApplyAsync(core, await SignedAsync(core, UnsignedRecall(full: true)), Recall);

        Assert.Equal(IncomingCompositionStatus.Terminal, result.Status);
        Assert.Equal(InboundProcessingStatus.Processed, (await ReadReceiptAsync(core, journalId)).Status);
        var stored = await ReadOnlyAsync(core);
        Assert.Equal((Recall, "RCL-IN-1"), (stored.Transfer.Kind, stored.Transfer.Key));
        Assert.Equal(CoreOutcome.NotSubmitted, stored.Transfer.CoreStatus);
        Assert.Equal(core.Clock.Now, stored.NextActionAtUtc);
        Assert.Equal(core.Clock.Now + new IncomingReconciliationOptions().Window, stored.DeadlineUtc);
        Assert.Equal(["incoming-transfer.registered"], stored.Events);
        var expected = new IncomingCamt056(
            "RCL-IN-1", new DateTimeOffset(2026, 10, 4, 14, 0, 0, TimeSpan.FromHours(4)), "CXL-IN-1", "P008-IN-1", "E2E-IN-1", "TX-IN-1",
            100.5m, "GEL", new DateOnly(2026, 10, 3), "DUPL",
            new RecallOriginalInput
            {
                SettlementDate = new DateOnly(2026, 10, 3),
                Remittance = new() { Unstructured = "Invoice 9", CreditorReference = new() { Issuer = "ISS", Reference = "REF-9" } },
                UltimateDebtor = new() { Name = "Ultimate Payer", Type = 0, Identifier = "111222333" },
                Debtor = new()
                {
                    Name = "Original Payer",
                    Address = new() { TownName = "Tbilisi", Country = "GE", AddressLines = ["Rustaveli 1"] },
                    Type = 1,
                    Identifier = "01001",
                    Account = "GE95TB0000000123456789"
                },
                DebtorAgent = new() { Bic = Other, Name = "TBC Bank" },
                CreditorAgent = new() { Bic = Participant },
                Creditor = new() { Name = "Original Payee", Account = "GE29NB0000000101904917" },
                UltimateCreditor = new() { Name = "Ultimate Payee" }
            });
        Assert.Equal(expected.Frozen(), stored.Content);
    }

    [Fact]
    public async Task A_verified_cancellation_request_is_stored_with_the_content_the_core_will_receive()
    {
        await using var core = await ProcessingHarness.CreateAsync();

        var (result, _) = await ApplyAsync(core, await SignedAsync(core, UnsignedCancellation(full: true)), Cancellation);

        Assert.Equal(IncomingCompositionStatus.Terminal, result.Status);
        var stored = await ReadOnlyAsync(core);
        Assert.Equal((Cancellation, "CNCL-IN-1"), (stored.Transfer.Kind, stored.Transfer.Key));
        var expected = new IncomingCamt055(
            "CNCL-IN-1", "PISPGE22", "NBGEGE22", new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero), "PCXL-1", "PMTINF-1",
            new IncomingCancelledGroup("PAIN001-MSG-1", "pain.001.001.12", new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero)),
            "CXL-1", "INS-1", "PISP-E2E-1",
            new IncomingCancellationReason("PISP LLC", "PISPGE22", "CUST", "Customer asked"),
            new IncomingCancelledInitiation(
                "GEL", 25.5m, new DateOnly(2026, 10, 4), "INST", "INST", "OTHR", "Invoice 77",
                new RecallAgentInput { Bic = Participant }, new RecallAgentInput { Bic = Other },
                new IncomingCancelledCreditor("Receiver LLC", "400000002", new RecallAddressInput { TownName = "Tbilisi", Country = "GE" }),
                "GE29NB0000000101904917"));
        Assert.Equal(expected.Frozen(), stored.Content);
    }

    [Fact]
    public async Task The_registry_spelling_of_the_cancellation_definition_is_accepted_and_version_08_is_held()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var spelled = UnsignedCancellation(definition: "camt.055.001.012");
        var older = UnsignedCancellation(id: "CNCL-IN-2", definition: "camt.055.001.08");

        var (accepted, _) = await ApplyAsync(core, await SignedAsync(core, spelled), "camt.055.001.012", sequence: 1);
        var (held, journalId) = await ApplyAsync(core, await SignedAsync(core, older), "camt.055.001.08", sequence: 2);

        Assert.Equal(IncomingCompositionStatus.Terminal, accepted.Status);
        Assert.Equal(IncomingCompositionStatus.Held, held.Status);
        Assert.Equal("Unsupported message definition.", (await ReadReceiptAsync(core, journalId)).HoldReason);
    }

    public static TheoryData<string, string, string> Unverifiable()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var type in new[] { Recall, Cancellation, Refusal })
        {
            data.Add(type, "definition", "Unsupported message definition.");
            data.Add(type, "schema", "Malformed or unsupported");
            data.Add(type, "signature", "Untrusted message signature.");
            data.Add(type, "several-transactions", "exactly one");
            data.Add(type, "not-ours", "not addressed to our participant");
            // Assgnmt/Id is a Max35Text, so the schema holds a longer key.
            data.Add(type, "key-too-long", "Malformed or unsupported");
            data.Add(type, "malformed", "Malformed or unsupported");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Unverifiable))]
    public async Task Anything_unverifiable_is_held_and_nothing_is_stored_recorded_or_delivered(string type, string problem, string reason)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var recallId = await AcceptedRecallAsync(core);
        var xml = problem switch
        {
            "definition" => await SignedAsync(core, Unsigned(type, definition: type == Recall ? "camt.056.001.10" : type == Refusal ? "camt.029.001.12" : "camt.055.001.11")),
            "schema" => await SignedAsync(core, Unsigned(type).Replace("<camt:SttlmMtd>CLRG</camt:SttlmMtd>", "<camt:SttlmMtd>NOPE</camt:SttlmMtd>").Replace("<camt:Cd>INST</camt:Cd></camt:SvcLvl>", "<camt:Cd>TOOLONGCODE</camt:Cd></camt:SvcLvl>")),
            "signature" => await SignedAsync(core, Unsigned(type), core.SigningCertificate),
            "several-transactions" => await SignedAsync(core, Unsigned(type, transactions: 2)),
            "not-ours" => await SignedAsync(core, Unsigned(type, ourBic: Other)),
            "key-too-long" => await SignedAsync(core, Unsigned(type, id: new string('K', 36))),
            _ => "<Message>"
        };

        var (result, journalId) = await ApplyAsync(core, xml, type);

        Assert.Equal(IncomingCompositionStatus.Held, result.Status);
        var receipt = await ReadReceiptAsync(core, journalId);
        Assert.Equal(InboundProcessingStatus.Held, receipt.Status);
        Assert.Contains(reason, receipt.HoldReason);
        await AssertNothingStoredOrRecordedAsync(core, recallId);
    }

    // camt.029 can also carry transactions inside an original payment information block (OrgnlPmtInfAndSts). Such a message
    // is schema-valid, so a nested transaction must count rather than be ignored beside the direct one, or instead of it.
    [Theory]
    [InlineData("beside")]
    [InlineData("only")]
    public async Task A_refusal_with_a_transaction_nested_in_an_original_payment_information_block_is_held(string nesting)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var recallId = await AcceptedRecallAsync(core);
        var xml = await SignedAsync(core, UnsignedRefusal(nesting: nesting));
        Pacs008Schema.ValidateCamt029(xml);

        var (result, journalId) = await ApplyAsync(core, xml, Refusal);

        Assert.Equal(IncomingCompositionStatus.Held, result.Status);
        Assert.Equal("camt.029 must answer exactly one transaction.", (await ReadReceiptAsync(core, journalId)).HoldReason);
        await AssertNothingStoredOrRecordedAsync(core, recallId);
    }

    public static TheoryData<string, string, bool> AtValidityBounds()
    {
        var data = new TheoryData<string, string, bool>();
        foreach (var type in new[] { Recall, Cancellation, Refusal })
        {
            foreach (var moment in SignatureValidity.Moments())
            {
                data.Add(type, (string)moment[0], (bool)moment[1]);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AtValidityBounds))]
    // Judged as of the receipt: processing after the certificate expired does not matter.
    public async Task A_message_received_outside_its_certificate_validity_is_held_and_nothing_is_stored(string type, string receivedAt, bool valid)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var recallId = await AcceptedRecallAsync(core);
        var xml = await SignedAsync(core, Unsigned(type));
        core.Clock.Now = SignatureValidity.LongAfterExpiry(core.IpsCertificate);

        var (result, journalId) = await ApplyAsync(core, xml, type, receivedAt: SignatureValidity.At(core.IpsCertificate, receivedAt));

        if (valid)
        {
            Assert.Equal(IncomingCompositionStatus.Terminal, result.Status);
            Assert.Equal(type, (await ReadOnlyAsync(core)).Transfer.Kind);
            return;
        }

        Assert.Equal(IncomingCompositionStatus.Held, result.Status);
        Assert.Equal(SignatureValidity.Stored(SignatureValidity.IncomingHold(core.IpsCertificate)), (await ReadReceiptAsync(core, journalId)).HoldReason);
        await AssertNothingStoredOrRecordedAsync(core, recallId);
    }

    [Theory]
    [InlineData(Recall)]
    [InlineData(Cancellation)]
    public async Task A_redelivery_is_one_transfer_and_changed_content_under_the_same_key_is_held(string type)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var xml = await SignedAsync(core, Unsigned(type, full: true));
        await ApplyAsync(core, xml, type, sequence: 1);
        var before = await ReadOnlyAsync(core);

        var (same, _) = await ApplyAsync(core, xml, type, sequence: 2);
        var (changed, journalId) = await ApplyAsync(core, await SignedAsync(core, Unsigned(type, full: true, amount: "200")), type, sequence: 3);

        Assert.Equal(IncomingCompositionStatus.Terminal, same.Status);
        Assert.Equal(IncomingCompositionStatus.Held, changed.Status);
        Assert.Equal(IncomingTransferRegistration.ConflictReason, (await ReadReceiptAsync(core, journalId)).HoldReason);
        var after = await ReadOnlyAsync(core);
        Assert.Equal(before.Content, after.Content);
        Assert.Equal(before.Events, after.Events);
    }

    [Theory]
    [InlineData(Recall, "RCL-IN-1")]
    [InlineData(Cancellation, "CNCL-IN-1")]
    public async Task The_core_is_asked_once_under_the_message_id_and_its_answer_is_final(string type, string key)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var cbs = new RecordingCore { Submit = _ => Answer("ACCP", key) };
        var id = await RegisterAsync(core, type);

        await ProcessAsync(core, id, cbs);
        await ProcessAsync(core, id, cbs);

        Assert.Equal([("submit", key)], cbs.Calls);
        var stored = await ReadOnlyAsync(core);
        Assert.Equal(CoreOutcome.Accepted, stored.Transfer.CoreStatus);
        Assert.Null(stored.NextActionAtUtc);
        Assert.Equal(["incoming-transfer.registered", "incoming-transfer.submission-started", "incoming-transfer.core-outcome-recorded"], stored.Events);
    }

    [Theory]
    [InlineData(Recall, "RCL-IN-1")]
    [InlineData(Cancellation, "CNCL-IN-1")]
    public async Task An_unanswered_call_is_followed_by_status_questions_and_a_core_that_never_saw_it_gets_it_again(string type, string key)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var options = new IncomingReconciliationOptions();
        var cbs = new RecordingCore
        {
            Submit = _ => Task.FromResult(new CoreResponse(503, "down")),
            Query = _ => Answer("PDNG", key)
        };
        var id = await RegisterAsync(core, type);
        await ProcessAsync(core, id, cbs);
        Assert.Equal(core.Clock.Now + options.RetryDelay(1), (await ReadOnlyAsync(core)).NextActionAtUtc);

        core.Clock.Now += options.RetryDelay(1);
        await ProcessAsync(core, id, cbs);
        Assert.Equal(core.Clock.Now + options.RetryDelay(2), (await ReadOnlyAsync(core)).NextActionAtUtc);

        cbs.Query = _ => Task.FromResult(new CoreResponse(404, "unknown reference"));
        core.Clock.Now += options.RetryDelay(2);
        await ProcessAsync(core, id, cbs);
        Assert.Equal(CoreOutcome.NotSubmitted, (await ReadOnlyAsync(core)).Transfer.CoreStatus);

        cbs.Submit = _ => Answer("ACCP", key);
        await ProcessAsync(core, id, cbs);

        Assert.Equal(CoreOutcome.Accepted, (await ReadOnlyAsync(core)).Transfer.CoreStatus);
        Assert.Equal([("submit", key), ("query", key), ("query", key), ("submit", key)], cbs.Calls);
        Assert.Equal(cbs.Transfers[0], cbs.Transfers[1]);
    }

    [Theory]
    [InlineData(Recall)]
    [InlineData(Cancellation)]
    public async Task The_window_end_goes_to_manual_review_without_another_call(string type)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var cbs = new RecordingCore { Submit = _ => Task.FromResult(new CoreResponse(503, "down")), Query = _ => Task.FromResult(new CoreResponse(503, "down")) };
        var id = await RegisterAsync(core, type);
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

    // Two instances share the database: the second cannot claim the transfer while the first is calling the core.
    [Theory]
    [InlineData(Recall)]
    [InlineData(Cancellation)]
    public async Task Two_instances_deliver_it_once(string type)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var key = type == Recall ? "RCL-IN-1" : "CNCL-IN-1";
        var cbs = new RecordingCore { Submit = async _ => { await gate.Task; return await Answer("ACCP", key); } };
        var id = await RegisterAsync(core, type);
        var first = ProcessAsync(core, id, cbs);
        await WaitUntilAsync(() => cbs.Calls.Count == 1);

        await ProcessAsync(core, id, cbs);
        gate.SetResult();
        await first;

        Assert.Single(cbs.Calls);
        Assert.Equal(CoreOutcome.Accepted, (await ReadOnlyAsync(core)).Transfer.CoreStatus);
    }

    [Fact]
    public async Task A_refusal_of_our_recall_records_one_refusal_and_is_stored_with_our_client_reference()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var recallId = await AcceptedRecallAsync(core);
        var callbacks = await CallbacksAsync(core, recallId);
        var before = await core.ReadAsync(recallId);

        var (result, journalId) = await ApplyAsync(core, await SignedAsync(core, UnsignedRefusal()), Refusal);

        Assert.Equal(IncomingCompositionStatus.Terminal, result.Status);
        Assert.Equal(InboundProcessingStatus.Processed, (await ReadReceiptAsync(core, journalId)).Status);
        var refusal = Assert.IsType<IncomingCamt029>((await ReadOnlyAsync(core)).Content);
        Assert.Equal(("RJCR-IN-1", "CXLSTS-IN-1", OurRecallId, "processing"), (refusal.MessageId, refusal.CancellationStatusId, refusal.OriginalMessageId, refusal.RecallClientReference));
        Assert.Equal(("NOAS", "No answer from the payee", 100.5m, "GEL"), (refusal.ReasonCode, refusal.AdditionalInformation, refusal.Original.Amount, refusal.Original.Currency));
        var recall = await core.ReadAsync(recallId);
        Assert.Equal(TransactionStatus.Accepted, recall.Payment.CurrentStatus);
        Assert.Equal(before.Payment.Current, recall.Payment.Current);
        Assert.Equal(core.Clock.Now, recall.Payment.RecallRefusedAtUtc);
        Assert.Equal([.. before.Events, "payment.recall-refused"], recall.Events);
        Assert.Equal(callbacks, await CallbacksAsync(core, recallId));
        var payload = JsonDocument.Parse(await RefusalPayloadAsync(core, recallId)).RootElement;
        Assert.Equal(("NOAS", "RJCR-IN-1", "CXLSTS-IN-1"),
            (payload.GetProperty("reasonCode").GetString(), payload.GetProperty("messageId").GetString(), payload.GetProperty("cancellationStatusId").GetString()));
    }

    [Fact]
    public async Task A_repeated_refusal_is_recorded_once()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var recallId = await AcceptedRecallAsync(core);
        var xml = await SignedAsync(core, UnsignedRefusal());
        await ApplyAsync(core, xml, Refusal, sequence: 1);
        var firstRefusedAt = (await core.ReadAsync(recallId)).Payment.RecallRefusedAtUtc;
        core.Clock.Now += TimeSpan.FromMinutes(1);

        // IPS redelivers the same refusal, then the creditor bank refuses the same recall again with a new message.
        var (redelivered, _) = await ApplyAsync(core, xml, Refusal, sequence: 2);
        var (another, _) = await ApplyAsync(core, await SignedAsync(core, UnsignedRefusal(id: "RJCR-IN-2", statusId: "CXLSTS-IN-2")), Refusal, sequence: 3);

        Assert.Equal(IncomingCompositionStatus.Terminal, redelivered.Status);
        Assert.Equal(IncomingCompositionStatus.Terminal, another.Status);
        var recall = await core.ReadAsync(recallId);
        Assert.Single(recall.Events, name => name == "payment.recall-refused");
        Assert.Equal(firstRefusedAt, recall.Payment.RecallRefusedAtUtc);
        await using var session = core.Database.Session();
        Assert.Equal(["RJCR-IN-1", "RJCR-IN-2"], await session.Context.Set<IncomingTransfer>().Select(transfer => transfer.Key).OrderBy(key => key).ToListAsync());
    }

    // An uncertain recall under a live claim belongs to its owner: changing it now would make the owner's commit fail and
    // discard its work, so the refusal waits until the claim ends.
    [Fact]
    public async Task A_refusal_of_a_recall_its_owner_is_working_on_waits_for_the_owner_and_is_then_recorded()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var recallId = await core.AcceptCamt056Async();
        await Assert.ThrowsAsync<SimulatedCrash>(() => core.ProcessAsync(recallId, default, new CrashOnSave(entry => entry.Entity.IsFinal)));
        var before = await core.ReadAsync(recallId);
        var xml = await SignedAsync(core, UnsignedRefusal());

        var (deferred, deferredJournal) = await ApplyAsync(core, xml, Refusal, sequence: 1);

        Assert.Equal(IncomingCompositionStatus.Deferred, deferred.Status);
        Assert.Equal(InboundProcessingStatus.Pending, (await ReadReceiptAsync(core, deferredJournal)).Status);
        var waiting = await core.ReadAsync(recallId);
        Assert.Null(waiting.Payment.RecallRefusedAtUtc);
        Assert.Equal(before.Events, waiting.Events);
        Assert.Equal(before.ClaimToken, waiting.ClaimToken);
        await using (var session = core.Database.Session())
        {
            Assert.False(await session.Context.Set<IncomingTransfer>().AnyAsync());
        }

        core.Clock.Now += TimeSpan.FromHours(1);
        var (recorded, _) = await ApplyAsync(core, xml, Refusal, sequence: 2);

        Assert.Equal(IncomingCompositionStatus.Terminal, recorded.Status);
        Assert.Equal(core.Clock.Now, (await core.ReadAsync(recallId)).Payment.RecallRefusedAtUtc);
    }

    public static TheoryData<string, string> Unmatched() => new()
    {
        { "no-original-group", IncomingRecallRefusals.NoOriginalGroup },
        { "unknown-recall", IncomingRecallRefusals.UnknownRecall },
        { "not-a-recall", IncomingRecallRefusals.UnknownRecall },
        { "other-end-to-end-id", IncomingRecallRefusals.DisagreeingIdentifiers },
        { "other-transaction-id", IncomingRecallRefusals.DisagreeingIdentifiers },
        { "not-a-refusal", "Only a refusal (RJCR)" }
    };

    [Theory]
    [MemberData(nameof(Unmatched))]
    public async Task A_refusal_that_matches_no_recall_of_ours_is_held_and_nothing_is_recorded_or_delivered(string problem, string reason)
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var recallId = await AcceptedRecallAsync(core);
        // A pacs.009 we sent: its message id is ours, but it is not a recall.
        await core.AcceptPacs009Async("pacs009");
        var transferMessageId = Pacs009Fixture.Request("pacs009").Id!;
        var unsigned = problem switch
        {
            "no-original-group" => UnsignedRefusal(originalMessageId: null),
            "unknown-recall" => UnsignedRefusal(originalMessageId: "RCL-UNKNOWN"),
            "not-a-recall" => UnsignedRefusal(originalMessageId: transferMessageId),
            "other-end-to-end-id" => UnsignedRefusal(endToEndId: "OTHER-E2E"),
            "other-transaction-id" => UnsignedRefusal(transactionId: "OTHER-TX"),
            _ => UnsignedRefusal(status: "ACCR")
        };
        var callbacks = await CallbacksAsync(core, recallId);

        var (result, journalId) = await ApplyAsync(core, await SignedAsync(core, unsigned), Refusal);

        Assert.Equal(IncomingCompositionStatus.Held, result.Status);
        Assert.Contains(reason, (await ReadReceiptAsync(core, journalId)).HoldReason);
        await AssertNothingStoredOrRecordedAsync(core, recallId);
        Assert.Equal(callbacks, await CallbacksAsync(core, recallId));
    }

    [Fact]
    public async Task A_crash_between_matching_and_commit_leaves_nothing_half_done()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var recallId = await AcceptedRecallAsync(core);
        var xml = await SignedAsync(core, UnsignedRefusal());

        await Assert.ThrowsAsync<SimulatedCrash>(() => ApplyAsync(core, xml, Refusal, sequence: 1, interceptor: new CrashWhenTransferIsSaved()));

        await AssertNothingStoredOrRecordedAsync(core, recallId);
        var (redelivered, _) = await ApplyAsync(core, xml, Refusal, sequence: 2);
        Assert.Equal(IncomingCompositionStatus.Terminal, redelivered.Status);
        Assert.Single((await core.ReadAsync(recallId)).Events, name => name == "payment.recall-refused");
    }

    [Fact]
    public async Task Worker_pulls_stores_acknowledges_and_delivers_a_recall_request_under_its_message_id()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var xml = await SignedAsync(core, UnsignedRecall(full: true));
        var run = await RunWorkerAsync(core, [("31", Recall, xml)], "/api/ips/camt056/receive", acceptFirst: true);

        Assert.Equal(["31"], run.Acknowledgements);
        var submission = Assert.Single(run.Submissions);
        Assert.Equal("RCL-IN-1", submission.Key);
        Assert.Contains("\"Id\":\"RCL-IN-1\"", submission.Body);
        Assert.Contains("\"RecallId\":\"CXL-IN-1\"", submission.Body);
        Assert.Contains("\"CreditorAgent\":{\"BICFI\":\"BAGAGE22\",\"Name\":null}", submission.Body);
        // The core keys a delivered recall on its message id; the client reference is the core's own, for its sends.
        Assert.Contains("\"ClientReference\":null", submission.Body);
    }

    [Fact]
    public async Task Worker_delivers_a_cancellation_request_and_asks_for_its_status_by_cancellation_kind()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var xml = await SignedAsync(core, UnsignedCancellation(full: true));
        var run = await RunWorkerAsync(core, [("32", Cancellation, xml)], "/api/ips/camt055/receive", acceptFirst: false);

        Assert.Equal(["32"], run.Acknowledgements);
        var submission = Assert.Single(run.Submissions);
        Assert.Equal("CNCL-IN-1", submission.Key);
        Assert.Contains("\"MsgId\":\"CNCL-IN-1\"", submission.Body);
        Assert.Contains("\"PostalAddress\":{\"TownName\":\"Tbilisi\",\"Country\":\"GE\"}", submission.Body);
        Assert.Equal("?messageKind=Camt055&reference=CNCL-IN-1", run.Queries.First());
    }

    [Fact]
    public async Task Worker_delivers_a_matched_refusal_with_our_recalls_client_reference_and_records_it_once()
    {
        await using var core = await ProcessingHarness.CreateAsync();
        var recallId = await AcceptedRecallAsync(core);
        var xml = await SignedAsync(core, UnsignedRefusal());
        var run = await RunWorkerAsync(core, [("33", Refusal, xml), ("33", Refusal, xml)], "/api/ips/camt029/receive", acceptFirst: true);

        Assert.Equal(["33", "33"], run.Acknowledgements);
        var submission = Assert.Single(run.Submissions);
        Assert.Equal("RJCR-IN-1", submission.Key);
        Assert.Contains("\"ClientReference\":\"processing\"", submission.Body);
        Assert.Contains("\"OriginalMessageId\":\"RCL-processing\"", submission.Body);
        Assert.Single((await core.ReadAsync(recallId)).Events, name => name == "payment.recall-refused");
    }

    private static string Unsigned(string type, bool full = false, string? id = null, string ourBic = Participant, int transactions = 1, string? definition = null, string amount = "100.5") =>
        type switch
        {
            Recall => UnsignedRecall(full, id ?? "RCL-IN-1", ourBic, transactions, definition ?? "camt.056.001.11", amount),
            Cancellation => UnsignedCancellation(full, id ?? "CNCL-IN-1", ourBic, transactions, definition ?? "camt.055.001.12", amount),
            _ => UnsignedRefusal(id: id ?? "RJCR-IN-1", ourBic: ourBic, transactions: transactions, definition: definition ?? "camt.029.001.13")
        };

    // A camt.056 another bank sends about a pacs.008 we received: we are the creditor's agent.
    private static string UnsignedRecall(
        bool full = false,
        string id = "RCL-IN-1",
        string ourBic = Participant,
        int transactions = 1,
        string definition = "camt.056.001.11",
        string amount = "100.5")
    {
        var remittance = full
            ? "<camt:RmtInf><camt:Ustrd>Invoice 9</camt:Ustrd><camt:Strd><camt:CdtrRefInf><camt:Tp><camt:CdOrPrtry><camt:Cd>SCOR</camt:Cd></camt:CdOrPrtry><camt:Issr>ISS</camt:Issr></camt:Tp><camt:Ref>REF-9</camt:Ref></camt:CdtrRefInf></camt:Strd></camt:RmtInf>"
            : string.Empty;
        var ultimateDebtor = full ? "<camt:UltmtDbtr><camt:Pty><camt:Nm>Ultimate Payer</camt:Nm><camt:Id><camt:OrgId><camt:Othr><camt:Id>111222333</camt:Id></camt:Othr></camt:OrgId></camt:Id></camt:Pty></camt:UltmtDbtr>" : string.Empty;
        var debtorDetails = full
            ? "<camt:PstlAdr><camt:TwnNm>Tbilisi</camt:TwnNm><camt:Ctry>GE</camt:Ctry><camt:AdrLine>Rustaveli 1</camt:AdrLine></camt:PstlAdr><camt:Id><camt:PrvtId><camt:Othr><camt:Id>01001</camt:Id></camt:Othr></camt:PrvtId></camt:Id>"
            : string.Empty;
        var debtorAgentName = full ? "<camt:Nm>TBC Bank</camt:Nm>" : string.Empty;
        var ultimateCreditor = full ? "<camt:UltmtCdtr><camt:Pty><camt:Nm>Ultimate Payee</camt:Nm></camt:Pty></camt:UltmtCdtr>" : string.Empty;
        var transaction =
            "<camt:TxInf><camt:CxlId>CXL-IN-1</camt:CxlId>" +
            "<camt:OrgnlGrpInf><camt:OrgnlMsgId>P008-IN-1</camt:OrgnlMsgId><camt:OrgnlMsgNmId>pacs.008.001.12</camt:OrgnlMsgNmId></camt:OrgnlGrpInf>" +
            "<camt:OrgnlEndToEndId>E2E-IN-1</camt:OrgnlEndToEndId><camt:OrgnlTxId>TX-IN-1</camt:OrgnlTxId>" +
            $"<camt:OrgnlIntrBkSttlmAmt Ccy=\"GEL\">{amount}</camt:OrgnlIntrBkSttlmAmt><camt:OrgnlIntrBkSttlmDt>2026-10-03</camt:OrgnlIntrBkSttlmDt>" +
            $"<camt:CxlRsnInf><camt:Orgtr><camt:Id><camt:OrgId><camt:AnyBIC>{Other}</camt:AnyBIC></camt:OrgId></camt:Id></camt:Orgtr><camt:Rsn><camt:Cd>DUPL</camt:Cd></camt:Rsn></camt:CxlRsnInf>" +
            "<camt:OrgnlTxRef><camt:IntrBkSttlmDt>2026-10-03</camt:IntrBkSttlmDt>" + SettlementAndType +
            $"{remittance}{ultimateDebtor}<camt:Dbtr><camt:Pty><camt:Nm>Original Payer</camt:Nm>{debtorDetails}</camt:Pty></camt:Dbtr>" +
            $"<camt:DbtrAcct>{Iban("GE95TB0000000123456789")}</camt:DbtrAcct><camt:DbtrAgt><camt:FinInstnId><camt:BICFI>{Other}</camt:BICFI>{debtorAgentName}</camt:FinInstnId></camt:DbtrAgt>" +
            $"<camt:CdtrAgt>{Agent(ourBic)}</camt:CdtrAgt><camt:Cdtr><camt:Pty><camt:Nm>Original Payee</camt:Nm></camt:Pty></camt:Cdtr>" +
            $"<camt:CdtrAcct>{Iban("GE29NB0000000101904917")}</camt:CdtrAcct>{ultimateCreditor}</camt:OrgnlTxRef></camt:TxInf>";
        var document =
            $"<camt:FIToFIPmtCxlReq>{Assignment(id, Other, "2026-10-04T14:00:00+04:00")}" +
            $"<camt:Undrlyg>{string.Concat(Enumerable.Repeat(transaction, transactions))}</camt:Undrlyg></camt:FIToFIPmtCxlReq>";
        return Envelope(definition, id, "urn:iso:std:iso:20022:tech:xsd:camt.056.001.11", document);
    }

    // A creditor bank's answer to the recall our core sent (Camt056Fixture): we are the debtor's agent.
    private static string UnsignedRefusal(
        string id = "RJCR-IN-1",
        string statusId = "CXLSTS-IN-1",
        string? originalMessageId = OurRecallId,
        string endToEndId = RecalledEndToEndId,
        string transactionId = RecalledTransactionId,
        string status = "RJCR",
        string ourBic = Participant,
        int transactions = 1,
        string definition = "camt.029.001.13",
        string nesting = "")
    {
        var group = originalMessageId is null
            ? string.Empty
            : $"<camt:OrgnlGrpInf><camt:OrgnlMsgId>{originalMessageId}</camt:OrgnlMsgId><camt:OrgnlMsgNmId>camt.056.001.11</camt:OrgnlMsgNmId></camt:OrgnlGrpInf>";
        var transaction =
            $"<camt:TxInfAndSts><camt:CxlStsId>{statusId}</camt:CxlStsId>{group}" +
            $"<camt:OrgnlEndToEndId>{endToEndId}</camt:OrgnlEndToEndId><camt:OrgnlTxId>{transactionId}</camt:OrgnlTxId><camt:TxCxlSts>{status}</camt:TxCxlSts>" +
            $"<camt:CxlStsRsnInf><camt:Orgtr><camt:Id><camt:OrgId><camt:AnyBIC>{Other}</camt:AnyBIC></camt:OrgId></camt:Id></camt:Orgtr><camt:Rsn><camt:Cd>NOAS</camt:Cd></camt:Rsn>" +
            "<camt:AddtlInf>No answer from the payee</camt:AddtlInf></camt:CxlStsRsnInf>" +
            "<camt:OrgnlTxRef><camt:IntrBkSttlmAmt Ccy=\"GEL\">100.5</camt:IntrBkSttlmAmt><camt:IntrBkSttlmDt>2026-10-03</camt:IntrBkSttlmDt>" + SettlementAndType +
            $"<camt:Dbtr><camt:Pty><camt:Nm>Original Payer</camt:Nm></camt:Pty></camt:Dbtr><camt:DbtrAcct>{Iban("GE29NB0000000101904917")}</camt:DbtrAcct>" +
            $"<camt:DbtrAgt>{Agent(ourBic)}</camt:DbtrAgt><camt:CdtrAgt>{Agent(Other)}</camt:CdtrAgt>" +
            $"<camt:Cdtr><camt:Pty><camt:Nm>Original Payee</camt:Nm></camt:Pty></camt:Cdtr><camt:CdtrAcct>{Iban("GE95TB0000000123456789")}</camt:CdtrAcct></camt:OrgnlTxRef></camt:TxInfAndSts>";
        var confirmation = status == "RJCR" ? "RJCR" : "CNCL";
        var document =
            $"<camt:RsltnOfInvstgtn>{Assignment(id, Other, "2026-10-04T10:00:00Z")}<camt:Sts><camt:Conf>{confirmation}</camt:Conf></camt:Sts>" +
            $"<camt:CxlDtls>{Nested(nesting)}{(nesting == "only" ? string.Empty : string.Concat(Enumerable.Repeat(transaction, transactions)))}</camt:CxlDtls></camt:RsltnOfInvstgtn>";
        return Envelope(definition, id, "urn:iso:std:iso:20022:tech:xsd:camt.029.001.13", document);
    }

    // A second refusal inside an original payment information block, which the schema places before the direct transactions.
    private static string Nested(string nesting) => nesting == string.Empty
        ? string.Empty
        : "<camt:OrgnlPmtInfAndSts><camt:OrgnlPmtInfId>PMTINF-2</camt:OrgnlPmtInfId><camt:TxInfAndSts><camt:CxlStsId>CXLSTS-IN-2</camt:CxlStsId>" +
            $"<camt:OrgnlEndToEndId>{RecalledEndToEndId}</camt:OrgnlEndToEndId><camt:TxCxlSts>RJCR</camt:TxCxlSts></camt:TxInfAndSts></camt:OrgnlPmtInfAndSts>";

    // A PISP's cancellation of a pain.001 we received: we are the debtor's agent.
    private static string UnsignedCancellation(
        bool full = false,
        string id = "CNCL-IN-1",
        string ourBic = Participant,
        int transactions = 1,
        string definition = "camt.055.001.12",
        string amount = "25.50")
    {
        var group = full
            ? "<camt:OrgnlGrpInf><camt:OrgnlMsgId>PAIN001-MSG-1</camt:OrgnlMsgId><camt:OrgnlMsgNmId>pain.001.001.12</camt:OrgnlMsgNmId><camt:OrgnlCreDtTm>2026-10-04T09:00:00Z</camt:OrgnlCreDtTm></camt:OrgnlGrpInf>"
            : string.Empty;
        var creditor = full
            ? "<camt:Cdtr><camt:Pty><camt:Nm>Receiver LLC</camt:Nm><camt:PstlAdr><camt:TwnNm>Tbilisi</camt:TwnNm><camt:Ctry>GE</camt:Ctry></camt:PstlAdr><camt:Id><camt:OrgId><camt:Othr><camt:Id>400000002</camt:Id></camt:Othr></camt:OrgId></camt:Id></camt:Pty></camt:Cdtr>" +
              $"<camt:CdtrAcct>{Iban("GE29NB0000000101904917")}</camt:CdtrAcct>"
            : string.Empty;
        var transaction =
            "<camt:TxInf><camt:CxlId>CXL-1</camt:CxlId><camt:OrgnlInstrId>INS-1</camt:OrgnlInstrId><camt:OrgnlEndToEndId>PISP-E2E-1</camt:OrgnlEndToEndId>" +
            "<camt:CxlRsnInf><camt:Orgtr><camt:Nm>PISP LLC</camt:Nm><camt:Id><camt:OrgId><camt:AnyBIC>PISPGE22</camt:AnyBIC></camt:OrgId></camt:Id></camt:Orgtr>" +
            "<camt:Rsn><camt:Cd>CUST</camt:Cd></camt:Rsn><camt:AddtlInf>Customer asked</camt:AddtlInf></camt:CxlRsnInf>" +
            $"<camt:OrgnlTxRef><camt:Amt><camt:InstdAmt Ccy=\"GEL\">{amount}</camt:InstdAmt></camt:Amt><camt:ReqdExctnDt><camt:Dt>2026-10-04</camt:Dt></camt:ReqdExctnDt>" +
            "<camt:PmtTpInf><camt:SvcLvl><camt:Cd>INST</camt:Cd></camt:SvcLvl><camt:LclInstrm><camt:Cd>INST</camt:Cd></camt:LclInstrm><camt:CtgyPurp><camt:Cd>OTHR</camt:Cd></camt:CtgyPurp></camt:PmtTpInf>" +
            "<camt:RmtInf><camt:Ustrd>Invoice 77</camt:Ustrd></camt:RmtInf>" +
            $"<camt:DbtrAgt>{Agent(ourBic)}</camt:DbtrAgt><camt:CdtrAgt>{Agent(Other)}</camt:CdtrAgt>{creditor}</camt:OrgnlTxRef></camt:TxInf>";
        var document =
            $"<camt:CstmrPmtCxlReq>{Assignment(id, "PISPGE22", "2026-10-04T10:00:00Z")}<camt:Undrlyg><camt:OrgnlPmtInfAndCxl>" +
            $"<camt:PmtCxlId>PCXL-1</camt:PmtCxlId><camt:OrgnlPmtInfId>PMTINF-1</camt:OrgnlPmtInfId>{group}" +
            $"{string.Concat(Enumerable.Repeat(transaction, transactions))}</camt:OrgnlPmtInfAndCxl></camt:Undrlyg></camt:CstmrPmtCxlReq>";
        return Envelope(definition, id, "urn:iso:std:iso:20022:tech:xsd:camt.055.001.12", document);
    }

    private const string SettlementAndType =
        "<camt:SttlmInf><camt:SttlmMtd>CLRG</camt:SttlmMtd><camt:ClrSys><camt:Cd>IPS</camt:Cd></camt:ClrSys></camt:SttlmInf>" +
        "<camt:PmtTpInf><camt:SvcLvl><camt:Cd>INST</camt:Cd></camt:SvcLvl><camt:LclInstrm><camt:Cd>INST</camt:Cd></camt:LclInstrm></camt:PmtTpInf>";

    private static string Agent(string bic) => $"<camt:FinInstnId><camt:BICFI>{bic}</camt:BICFI></camt:FinInstnId>";

    private static string Iban(string iban) => $"<camt:Id><camt:IBAN>{iban}</camt:IBAN></camt:Id>";

    private static string Assignment(string id, string assigner, string created) =>
        $"<camt:Assgnmt><camt:Id>{id}</camt:Id><camt:Assgnr><camt:Agt>{Agent(assigner)}</camt:Agt></camt:Assgnr>" +
        $"<camt:Assgne><camt:Agt>{Agent("NBGEGE22")}</camt:Agt></camt:Assgne><camt:CreDtTm>{created}</camt:CreDtTm></camt:Assgnmt>";

    private static string Envelope(string definition, string id, string documentNamespace, string document) =>
        $"<Message xmlns:head=\"urn:iso:std:iso:20022:tech:xsd:head.001.001.03\" xmlns:camt=\"{documentNamespace}\"><head:AppHdr>" +
        "<head:Fr><head:FIId><head:FinInstnId><head:BICFI>NBGEGE22</head:BICFI></head:FinInstnId></head:FIId></head:Fr>" +
        $"<head:To><head:FIId><head:FinInstnId><head:BICFI>{Participant}</head:BICFI></head:FinInstnId></head:FIId></head:To>" +
        $"<head:BizMsgIdr>{id}</head:BizMsgIdr><head:MsgDefIdr>{definition}</head:MsgDefIdr><head:CreDt>2026-10-04T10:00:00.0000000Z</head:CreDt><head:Sgntr/></head:AppHdr>" +
        $"<camt:Document>{document}</camt:Document></Message>";

    private static async Task<string> SignedAsync(ProcessingHarness core, string unsigned, X509Certificate2? signer = null) =>
        (await IpsReplies.SignAsync(signer ?? core.IpsCertificate, unsigned))[0];

    // Our core's recall, accepted by IPS, as the one a refusal answers.
    private static async Task<Guid> AcceptedRecallAsync(ProcessingHarness core)
    {
        var id = await core.AcceptCamt056Async();
        Assert.Equal(TransactionStatus.Accepted, (await core.ProcessAsync(id))!.Status);
        return id;
    }

    private static async Task<(IncomingCompositionResult Result, Guid JournalId)> ApplyAsync(
        ProcessingHarness core,
        string xml,
        string messageType,
        long sequence = 1,
        DateTimeOffset? receivedAt = null,
        IInterceptor? interceptor = null)
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

        await using var session = core.Database.Session(interceptor is null ? [] : [interceptor]);
        var receipt = (await new InboundReceiptRepository(session.Context).ReadAsync(journalId, default))!.Receipt;
        var trust = new IpsSignatureTrust([core.IpsCertificate], core.Clock);
        var handler = new IncomingTransferRegistration(
            [
                new IncomingPacs009Protocol(trust), new IncomingPacs004Protocol(trust), new IncomingPain001Protocol(trust),
                new IncomingCamt056Protocol(trust), new IncomingCamt055Protocol(trust), new IncomingCamt029Protocol(trust)
            ],
            new IncomingTransferRepository(session.Context),
            new IncomingRecallRefusals(new OutgoingPaymentRepository(session.Context), new PaymentPreparationRepository(session.Context), session.Work),
            new InboundWorkRepository(session.Context),
            session.Unit,
            new IncomingReconciliationOptions(),
            new IncomingCompositionOptions(),
            core.Clock);
        return (await handler.ProcessAsync(claim, receipt, default), journalId);
    }

    private static async Task<Guid> RegisterAsync(ProcessingHarness core, string type)
    {
        await ApplyAsync(core, await SignedAsync(core, Unsigned(type)), type);
        return (await ReadOnlyAsync(core)).Transfer.Id;
    }

    private static async Task ProcessAsync(ProcessingHarness core, Guid transferId, RecordingCore cbs)
    {
        await using var session = core.Database.Session();
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
        return new(metadata.Transfer, snapshot.Content, snapshot.DeadlineUtc, metadata.NextActionAtUtc, events);
    }

    private static async Task AssertNothingStoredOrRecordedAsync(ProcessingHarness core, Guid recallId)
    {
        await using var session = core.Database.Session();
        Assert.False(await session.Context.Set<IncomingTransferMetadata>().AnyAsync());
        var recall = await core.ReadAsync(recallId);
        Assert.Null(recall.Payment.RecallRefusedAtUtc);
        Assert.DoesNotContain("payment.recall-refused", recall.Events);
    }

    private static async Task<int> CallbacksAsync(ProcessingHarness core, Guid paymentId)
    {
        await using var session = core.Database.Session();
        return await session.Context.Set<OutgoingStatusDeliveryRow>().CountAsync(row => row.PaymentId == paymentId);
    }

    private static async Task<string> RefusalPayloadAsync(ProcessingHarness core, Guid paymentId)
    {
        await using var session = core.Database.Session();
        return await session.Context.Set<TransactionEventRow>()
            .Where(row => row.TransactionId == paymentId && row.Name == "payment.recall-refused")
            .Select(row => row.PayloadJson)
            .SingleAsync();
    }

    private static async Task<StoredInboundReceipt> ReadReceiptAsync(ProcessingHarness core, Guid journalId)
    {
        await using var session = core.Database.Session();
        return (await new InboundReceiptRepository(session.Context).ReadAsync(journalId, default))!;
    }

    private static Task<CoreResponse> Answer(string status, string id) =>
        Task.FromResult(new CoreResponse(200, $"{{\"status\":\"{status}\",\"id\":\"{id}\"}}"));

    // Runs the receive, processing and follow-up workers against one simulated IPS and core until the delivery is accepted.
    // The core either accepts the first submission or answers 503 and then accepts on the status question.
    private static async Task<WorkerRun> RunWorkerAsync(ProcessingHarness core, (string Sequence, string Type, string Body)[] messages, string receivePath, bool acceptFirst)
    {
        var deliveries = new ConcurrentQueue<(string Sequence, string Type, string Body)>(messages);
        var run = new WorkerRun(new ConcurrentQueue<string?>(), new ConcurrentQueue<(string? Key, string Body)>(), new ConcurrentQueue<string>());
        await using var server = await HttpSimulator.StartAsync(async context =>
        {
            if (context.Request.Method == "GET" && context.Request.Path == "/Message")
            {
                if (deliveries.TryDequeue(out var delivery))
                {
                    context.Response.Headers["X-MONTRAN-IPS-MessageSeq"] = delivery.Sequence;
                    context.Response.Headers["X-MONTRAN-IPS-MessageType"] = delivery.Type;
                    await context.Response.WriteAsync(delivery.Body);
                }
                else
                {
                    context.Response.Headers["X-MONTRAN-IPS-ReqSts"] = "EMPTY";
                }
            }
            else if (context.Request.Method == "POST" && context.Request.Path == "/MessageAck")
            {
                run.Acknowledgements.Enqueue(context.Request.Headers["X-MONTRAN-IPS-MessageSeq"]);
            }
            else if (context.Request.Method == "POST" && context.Request.Path == receivePath)
            {
                run.Submissions.Enqueue((context.Request.Headers["Idempotency-Key"], await new StreamReader(context.Request.Body).ReadToEndAsync()));
                if (!acceptFirst)
                {
                    context.Response.StatusCode = 503;
                    return;
                }

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"status\":\"ACCP\"}");
            }
            else if (context.Request.Method == "GET" && context.Request.Path == "/api/ips/payments/status")
            {
                run.Queries.Enqueue(context.Request.QueryString.Value ?? string.Empty);
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"status\":\"ACCP\"}");
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
                return await session.Context.Set<IncomingTransfer>().AnyAsync(transfer => transfer.CoreStatus == CoreOutcome.Accepted)
                    && run.Acknowledgements.Count == messages.Length;
            });
        }
        finally
        {
            await host.StopAsync();
        }

        return run;
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
        builder.Services.AddSingleton(new IncomingReconciliationOptions(retryDelays: [TimeSpan.FromMilliseconds(50)]));
        builder.Services.AddIncomingHttpClients();
        builder.Services.AddIncomingWorkers();
        builder.Services.Configure<HostOptions>(options => options.ServicesStopConcurrently = true);
        return builder.Build();
    }

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

    private sealed record WorkerRun(ConcurrentQueue<string?> Acknowledgements, ConcurrentQueue<(string? Key, string Body)> Submissions, ConcurrentQueue<string> Queries);

    private sealed record StoredTransfer(
        IncomingTransfer Transfer,
        IIncomingTransferContent Content,
        DateTimeOffset DeadlineUtc,
        DateTimeOffset? NextActionAtUtc,
        IReadOnlyList<string> Events);

    // The core system: records each call and answers as scripted.
    private sealed class RecordingCore : IIncomingTransferCoreClient
    {
        private readonly object _gate = new();
        private readonly List<(string Kind, string Key)> _calls = [];
        private readonly List<IIncomingTransferContent> _transfers = [];

        public Func<IIncomingTransferContent, Task<CoreResponse>> Submit { get; set; } = _ => Task.FromResult(new CoreResponse(503, "down"));
        public Func<string, Task<CoreResponse>> Query { get; set; } = _ => Task.FromResult(new CoreResponse(503, "down"));

        public IReadOnlyList<(string Kind, string Key)> Calls
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

    // Fails the save that would commit the registered refusal together with the recorded refusal on the recall.
    private sealed class CrashWhenTransferIsSaved : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            eventData.Context!.ChangeTracker.Entries<IncomingTransfer>().Any(entry => entry.State == EntityState.Added)
                ? throw new SimulatedCrash()
                : ValueTask.FromResult(result);
    }
}
