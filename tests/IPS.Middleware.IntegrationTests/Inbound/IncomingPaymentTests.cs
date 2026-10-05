using System.Data.SqlTypes;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Inbound;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Repositories.Inbound;
using IPS.Middleware.Infrastructure.Repositories.Payments;
using IPS.Middleware.Infrastructure.Transactions;
using IPS.Middleware.Infrastructure.UnitOfWork;
using IPS.Middleware.IntegrationTests.Transactions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Inbound;

public sealed class IncomingPaymentTests
{
    private const string Participant = "BAGAGE22";
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(45);
    [Fact]
    public void Inbound_foundations_register_default_processing_options_without_replacing_a_host_choice()
    {
        var custom = new IncomingProcessingOptions(paymentWindow: TimeSpan.FromSeconds(30));
        using var defaults = new ServiceCollection().AddInboundFoundations().BuildServiceProvider();
        using var hosted = new ServiceCollection().AddSingleton(custom).AddInboundFoundations().BuildServiceProvider();
        Assert.Equal(TimeSpan.FromSeconds(20), defaults.GetRequiredService<IncomingProcessingOptions>().PaymentWindow);
        Assert.Same(custom, hosted.GetRequiredService<IncomingProcessingOptions>());
    }

    [Fact]
    public async Task Identical_deliveries_on_different_sequences_share_one_payment_and_keep_their_own_references()
    {
        await using var test = await Harness.CreateAsync();
        var first = await test.ReceiveAsync(1);
        var second = await test.ReceiveAsync(2);
        var created = await test.RegisterAsync(first, Incoming(message: "MSG-1"));
        // Equal contents restated: numerically equal amount, the same instant at another offset, and fresh lists.
        var existing = await test.RegisterAsync(second, Incoming(message: "MSG-2", change: r => r with { Amount = 12.5m, AcceptanceDateTime = Now.ToOffset(TimeSpan.FromHours(4)), InitiationChannelInstrument = r.InitiationChannelInstrument! with { InstrumentCodes = new List<string> { "QR", "NFC" } } }));
        Assert.Equal(IncomingRegistrationOutcome.Created, created.Outcome);
        Assert.Equivalent(new IncomingRegistration(IncomingRegistrationOutcome.Existing, created.PaymentId), existing, strict: true);
        await using var read = test.Database.Context();
        var payments = new IncomingPaymentRepository(read);
        Assert.Equal((created.PaymentId, "MSG-1"), await Attachment(read, first.JournalId));
        Assert.Equal((created.PaymentId, "MSG-2"), await Attachment(read, second.JournalId));
        var stored = (await payments.FindAsync(Participant, "E2E-1", default))!;
        Assert.Equal(TimeSpan.Zero, stored.Request.AcceptanceDateTime!.Value.Offset); // The canonical snapshot is not replaced.
        Assert.Equal(new[] { "incoming-payment.registered" }, await Events(read, created.PaymentId!.Value));
        foreach (var claim in new[]
        {
            first,
            second
        }

        ) // Attachment alone never completes a receipt.
        {
            Assert.Equal(InboundProcessingStatus.Pending, (await new InboundReceiptRepository(read).ReadAsync(claim.JournalId, default))!.Status);
        }
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("order")]
    [InlineData("missing")]
    [InlineData("case")]
    public async Task Conflicting_contents_hold_the_new_receipt_and_leave_the_canonical_payment_unchanged(string change)
    {
        await using var test = await Harness.CreateAsync();
        var first = await test.ReceiveAsync(1);
        var second = await test.ReceiveAsync(2);
        var original = Incoming();
        var created = await test.RegisterAsync(first, original);
        var conflict = await test.RegisterAsync(second, Incoming(message: "MSG-2", change: r => change switch
        {
            "amount" => r with { Amount = 12.51m },
            "order" => r with { InitiationChannelInstrument = r.InitiationChannelInstrument! with { InstrumentCodes = ["NFC", "QR"] } },
            "missing" => r with { PaymentInitiation = r.PaymentInitiation! with { Geolocation = null } },
            _ => r with { Creditor = r.Creditor! with { Name = "creditor" } }
        }));
        Assert.Equivalent(new IncomingRegistration(IncomingRegistrationOutcome.Conflict, created.PaymentId), conflict, strict: true);
        await using var read = test.Database.Context();
        var receipt = (await new InboundReceiptRepository(read).ReadAsync(second.JournalId, default))!;
        Assert.Equal(InboundProcessingStatus.Held, receipt.Status);
        Assert.Equal(IncomingPaymentIntake.ConflictReason, receipt.HoldReason);
        Assert.Null(receipt.NextActionAtUtc);
        var payments = new IncomingPaymentRepository(read);
        Assert.Equal((null, "MSG-2"), await Attachment(read, second.JournalId));
        Assert.Equal(Incoming(message: "MSG-2").Original,
            await new InboundReceiptRepository(read).ReadOriginalReferencesAsync(second.JournalId, default));
        Assert.True(original.HasSameContents((await payments.FindAsync(Participant, "E2E-1", default))!.Request));
        Assert.Single(await Events(read, created.PaymentId!.Value));
        Assert.Equal(new[] { first.JournalId }, await new InboundWorkRepository(read).FindDueAsync(Now + Lease, 10, default));
    }

    [Fact]
    public async Task Participant_case_and_trailing_characters_identify_distinct_payments()
    {
        await using var test = await Harness.CreateAsync();
        var identities = new[]
        {
            (Participant, "E2E-1"),
            ("OTHERBIC", "E2E-1"),
            (Participant, "E2E-1 "),
            (Participant, "e2e-1")
        };
        var ids = new List<Guid>();
        foreach (var (participant, reference) in identities)
        {
            var result = await test.RegisterAsync(await test.ReceiveAsync(ids.Count + 1, participant), Incoming(reference));
            Assert.Equal(IncomingRegistrationOutcome.Created, result.Outcome);
            ids.Add(result.PaymentId!.Value);
        }

        Assert.Equal(4, ids.Distinct().Count());
        Assert.Equivalent(new IncomingRegistration(IncomingRegistrationOutcome.Existing, ids[2]), await test.RegisterAsync(await test.ReceiveAsync(9), Incoming("E2E-1 ")), strict: true);
        await using var read = test.Database.Context();
        var payments = new IncomingPaymentRepository(read);
        foreach (var ((participant, reference), id) in identities.Zip(ids))
        {
            Assert.Equal(id, (await payments.FindAsync(participant, reference, default))!.Payment.Id);
        }

        Assert.Null(await payments.FindAsync(Participant, "E2E-1  ", default));
    }

    [Fact]
    public async Task Concurrent_registrations_converge_on_one_payment_and_one_event()
    {
        await using var test = await Harness.CreateAsync();
        var claims = new List<InboundClaim>();
        for (var sequence = 1; sequence <= 8; sequence++)
        {
            claims.Add(await test.ReceiveAsync(sequence));
        }

        var results = await Task.WhenAll(claims.Select((claim, index) => test.RegisterAsync(claim, Incoming(message: $"MSG-{index}"))));
        Assert.Single(results, result => result.Outcome == IncomingRegistrationOutcome.Created);
        Assert.All(results, result => Assert.Equal(results[0].PaymentId, result.PaymentId));
        await using var read = test.Database.Context();
        Assert.Equal(1, await Count(read, "SELECT COUNT(*) AS Value FROM IncomingPayments"));
        Assert.Single(await Events(read, results[0].PaymentId!.Value));
        foreach (var claim in claims)
        {
            Assert.Equal(results[0].PaymentId, (await Attachment(read, claim.JournalId)).PaymentId);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_lost_uniqueness_race_reuses_only_a_winner_with_matching_contents(bool conflicting)
    {
        var barrier = new SaveBarrier(2);
        await using var test = await Harness.CreateAsync(barrier);
        var claims = new[]
        {
            await test.ReceiveAsync(1),
            await test.ReceiveAsync(2)
        };
        var contents = new[]
        {
            Incoming(),
            conflicting ? Incoming(change: r => r with  { Amount = 99m }) : Incoming(message: "MSG-2")
        };
        barrier.Armed = true;
        var results = await Task.WhenAll(claims.Select((claim, index) => test.RegisterAsync(claim, contents[index])));
        var winner = Array.FindIndex(results, result => result.Outcome == IncomingRegistrationOutcome.Created);
        var loser = 1 - winner;
        Assert.InRange(winner, 0, 1);
        Assert.Equal(conflicting ? IncomingRegistrationOutcome.Conflict : IncomingRegistrationOutcome.Existing, results[loser].Outcome);
        Assert.Equal(results[winner].PaymentId, results[loser].PaymentId);
        await using var read = test.Database.Context();
        Assert.True(contents[winner].HasSameContents((await new IncomingPaymentRepository(read).FindAsync(Participant, "E2E-1", default))!.Request));
        Assert.Equal(conflicting ? null : results[winner].PaymentId, (await Attachment(read, claims[loser].JournalId)).PaymentId);
        var receipt = (await new InboundReceiptRepository(read).ReadAsync(claims[loser].JournalId, default))!;
        Assert.Equal(conflicting ? InboundProcessingStatus.Held : InboundProcessingStatus.Pending, receipt.Status);
        Assert.Single(await Events(read, results[winner].PaymentId!.Value));
    }

    [Fact]
    public async Task Repeating_a_receipt_registration_is_idempotent_and_cannot_move_it_to_another_payment()
    {
        await using var test = await Harness.CreateAsync();
        var claim = await test.ReceiveAsync(1);
        var created = await test.RegisterAsync(claim, Incoming());
        Assert.Equivalent(new IncomingRegistration(IncomingRegistrationOutcome.Existing, created.PaymentId), await test.RegisterAsync(claim, Incoming()), strict: true);
        await test.RegisterAsync(await test.ReceiveAsync(2), Incoming("E2E-2"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => test.RegisterAsync(claim, Incoming("E2E-2")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => test.RegisterAsync(claim, Incoming(message: "REPLACEMENT")));
        await using var read = test.Database.Context();
        Assert.Equal((created.PaymentId, "MSG-1"), await Attachment(read, claim.JournalId));
        Assert.Equal(Incoming().Original, await new InboundReceiptRepository(read).ReadOriginalReferencesAsync(claim.JournalId, default));
        Assert.Single(await Events(read, created.PaymentId!.Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_cancelled_conflict_commit_rolls_back_references_and_hold_together(bool cancel)
    {
        await using var test = await Harness.CreateAsync();
        var canonical = await test.RegisterNewAsync(1, "E2E-1");
        var claim = await test.ReceiveAsync(2);
        var conflict = Incoming(message: "CONFLICT", change: r => r with { Amount = 99m });
        using var cancellation = new CancellationTokenSource();
        await using (var db = test.Database.Context(cancel ? new CancelAfterEntities(cancellation) : new FailEventSave()))
        {
            var intake = Intake(db, Now);
            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => intake.RegisterAsync(claim, conflict, cancellation.Token));
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => intake.RegisterAsync(claim, conflict, default));
            }
        }

        await using (var read = test.Database.Context())
        {
            var receipts = new InboundReceiptRepository(read);
            var receipt = (await receipts.ReadAsync(claim.JournalId, default))!;
            Assert.Equal(InboundProcessingStatus.Pending, receipt.Status);
            Assert.Null(receipt.HoldReason);
            Assert.Equal((null, null), await Attachment(read, claim.JournalId));
            Assert.Null(await receipts.ReadOriginalReferencesAsync(claim.JournalId, default));
            Assert.Single(await Events(read, canonical));
        }
        Assert.Equivalent(new IncomingRegistration(IncomingRegistrationOutcome.Conflict, canonical), await test.RegisterAsync(claim, conflict), strict: true);
        await using var committed = test.Database.Context();
        var stored = new InboundReceiptRepository(committed);
        Assert.Equal(conflict.Original, await stored.ReadOriginalReferencesAsync(claim.JournalId, default));
        Assert.Equal(InboundProcessingStatus.Held, (await stored.ReadAsync(claim.JournalId, default))!.Status);
        Assert.Equal((null, "CONFLICT"), await Attachment(committed, claim.JournalId));
    }

    [Theory]
    [InlineData(false, null, true)]
    [InlineData(false, "{}", true)]
    [InlineData(false, "not-json", false)]
    [InlineData(true, null, false)]
    [InlineData(true, "{}", true)]
    [InlineData(true, "not-json", false)]
    public async Task Sql_attachment_constraint_requires_valid_references_for_an_attached_receipt(bool attached, string? json, bool allowed)
    {
        await using var test = await Harness.CreateAsync();
        var payment = await test.RegisterNewAsync(1, "E2E-1");
        var receipt = await test.ReceiveAsync(2);
        await using var sql = test.Database.Context();
        Guid? paymentId = attached ? payment : null;
        Task<int> Update() => sql.Database.ExecuteSqlAsync(
            $"UPDATE InboundMessageJournal SET IncomingPaymentId = {paymentId}, OriginalJson = {json} WHERE Id = {receipt.JournalId}");
        if (allowed)
        {
            Assert.Equal(1, await Update());
        }
        else
        {
            var error = await Assert.ThrowsAsync<SqlException>(Update);
            Assert.Equal(547, error.Number);
            Assert.Contains("CK_InboundJournal_Attachment", error.Message);
        }
    }

    [Fact]
    public async Task Saving_unattached_references_requires_live_ownership_and_is_write_once()
    {
        await using var test = await Harness.CreateAsync();
        var claim = await test.ReceiveAsync(1);
        var original = Incoming().Original;
        await using (var db = test.Database.Context())
        {
            var work = new InboundWorkRepository(db);
            Assert.False(await work.StageOriginalReferencesAsync(claim with { Token = Guid.NewGuid() }, original, Now, default));
            Assert.False(await work.StageOriginalReferencesAsync(claim, original, Now + Lease, default));
            Assert.Equal(0, await new UnitOfWork(db).SaveAsync());
            Assert.Null(await new InboundReceiptRepository(db).ReadOriginalReferencesAsync(claim.JournalId, default));
            Assert.True(await work.StageOriginalReferencesAsync(claim, original, Now, default));
            await new UnitOfWork(db).SaveAsync();
            Assert.True(await work.StageOriginalReferencesAsync(claim, original with { }, Now, default));
            Assert.Equal(0, await new UnitOfWork(db).SaveAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => work.StageOriginalReferencesAsync(claim, original with { BusinessMessageId = "CHANGED" }, Now, default));
            Assert.Equal(0, await new UnitOfWork(db).SaveAsync());
        }

        await using var read = test.Database.Context();
        var receipts = new InboundReceiptRepository(read);
        Assert.Equal(original, await receipts.ReadOriginalReferencesAsync(claim.JournalId, default));
        Assert.Null(await receipts.ReadOriginalReferencesAsync(Guid.NewGuid(), default));
        Assert.Equal((null, original.BusinessMessageId), await Attachment(read, claim.JournalId));
    }

    [Fact]
    public async Task Stale_receipt_owner_cannot_commit_original_references()
    {
        await using var test = await Harness.CreateAsync();
        var claim = await test.ReceiveAsync(1);
        await using var stale = test.Database.Context();
        Assert.True(await new InboundWorkRepository(stale).StageOriginalReferencesAsync(claim, Incoming().Original, Now, default));
        Assert.True(await test.HoldAsync(claim, "Held by the current writer."));
        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() => new UnitOfWork(stale).SaveAsync());
        await using var read = test.Database.Context();
        Assert.Null(await new InboundReceiptRepository(read).ReadOriginalReferencesAsync(claim.JournalId, default));
    }

    [Fact]
    public async Task Payment_ownership_is_committed_fenced_and_reacquired_only_after_expiry()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.RegisterNewAsync(1, "E2E-1");
        await using (var left = test.Database.Context())
        await using (var right = test.Database.Context())
        {
            Assert.NotNull(await new IncomingPaymentWorkRepository(left).StageClaimAsync(id, Now, Lease, default));
            Assert.NotNull(await new IncomingPaymentWorkRepository(right).StageClaimAsync(id, Now, Lease, default));
            await new UnitOfWork(left).SaveAsync();
            await Assert.ThrowsAsync<PersistenceConcurrencyException>(() => new UnitOfWork(right).SaveAsync());
        }

        Assert.Null(await test.AcquireAsync(id));
        test.Clock.Now = Now + Lease;
        await using var owner = test.Database.Context();
        var ownerWork = Work(owner, Now + Lease);
        var claim = (await ownerWork.AcquireAsync(id, Lease, default))!;
        Assert.False(await test.ReleaseAsync(claim with { Token = Guid.NewGuid() }, Now.AddMinutes(5)));
        test.Clock.Now = Now + Lease + Lease;
        Assert.False(await test.ReleaseAsync(claim, Now.AddMinutes(5)));
        var replacement = (await test.AcquireAsync(id))!;
        Assert.NotEqual(claim.Token, replacement.Token);
        // The expired owner still believes its claim is live; the row version rejects its stale write.
        Assert.False(await ownerWork.ReleaseAsync(claim, Now.AddMinutes(5), default));
        Assert.True(await test.ReleaseAsync(replacement, Now.AddMinutes(5)));
        await using var read = test.Database.Context();
        var work = new IncomingPaymentWorkRepository(read);
        Assert.Empty(await work.FindDueAsync(Now.AddMinutes(4), 10, default));
        Assert.Equal(new[] { id }, await work.FindDueAsync(Now.AddMinutes(5), 10, default));
    }

    [Fact]
    public async Task Discovery_orders_due_payments_and_excludes_live_owners_and_future_work()
    {
        await using var test = await Harness.CreateAsync();
        test.Clock.Now = Now.AddSeconds(-2);
        var older = await test.RegisterNewAsync(1, "E2E-1");
        test.Clock.Now = Now.AddSeconds(-1);
        var sameTime = new[]
        {
            await test.RegisterNewAsync(2, "E2E-2"),
            await test.RegisterNewAsync(3, "E2E-3")
        };
        var future = await test.RegisterNewAsync(4, "E2E-4");
        test.Clock.Now = Now;
        Assert.NotNull(await test.AcquireAsync(sameTime[1]));
        Assert.True(await test.ReleaseAsync((await test.AcquireAsync(future))!, Now.AddMinutes(1)));
        await using var read = test.Database.Context();
        var work = new IncomingPaymentWorkRepository(read);
        Assert.Equal(new[] { older, sameTime[0] }, await work.FindDueAsync(Now, 10, default));
        Assert.Equal(new[] { older }, await work.FindDueAsync(Now, 1, default));
        var expected = sameTime.OrderBy(id => new SqlGuid(id)).Prepend(older).Append(future);
        Assert.Equal(expected, await work.FindDueAsync(Now.AddMinutes(1), 10, default));
    }

    [Fact]
    public async Task Duplicate_registration_preserves_payment_ownership_and_schedule()
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.RegisterNewAsync(1, "E2E-1");
        await using var owner = test.Database.Context();
        var work = Work(owner, Now);
        var claim = (await work.AcquireAsync(id, Lease, default))!;
        Assert.Equal(IncomingRegistrationOutcome.Existing, (await test.RegisterAsync(await test.ReceiveAsync(2), Incoming())).Outcome);
        await using (var read = test.Database.Context())
        {
            Assert.Empty(await new IncomingPaymentWorkRepository(read).FindDueAsync(Now, 10, default));
        }

        Assert.True(await work.ReleaseAsync(claim, Now.AddMinutes(1), default)); // The owner's loaded row version is still current.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_cancelled_commit_rolls_back_receipt_identity_payment_snapshot_and_events(bool cancel)
    {
        await using var test = await Harness.CreateAsync();
        var claim = await test.ReceiveAsync(1);
        using var cancellation = new CancellationTokenSource();
        await using (var db = test.Database.Context(cancel ? new CancelAfterEntities(cancellation) : new FailEventSave()))
        {
            var intake = Intake(db, Now);
            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => intake.RegisterAsync(claim, Incoming(), cancellation.Token));
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => intake.RegisterAsync(claim, Incoming(), default));
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => new UnitOfWork(db).SaveAsync());
        }

        await using (var read = test.Database.Context())
        {
            Assert.Equal(0, await Count(read, "SELECT COUNT(*) AS Value FROM IncomingPayments"));
            Assert.Equal(0, await Count(read, "SELECT COUNT(*) AS Value FROM AggregateIdentities"));
            Assert.Equal(0, await Count(read, "SELECT COUNT(*) AS Value FROM TransactionEvents"));
            Assert.Equal((null, null), await Attachment(read, claim.JournalId));
        }

        Assert.Equal(IncomingRegistrationOutcome.Created, (await test.RegisterAsync(claim, Incoming())).Outcome);
    }

    [Fact]
    public async Task Materialized_payments_raise_no_events_and_restore_an_immutable_frozen_snapshot()
    {
        await using var test = await Harness.CreateAsync();
        var claim = await test.ReceiveAsync(1);
        var incoming = Incoming();
        await using (var db = test.Database.Context())
        {
            await Intake(db, Now.ToOffset(TimeSpan.FromHours(4))).RegisterAsync(claim, incoming, default);
            Assert.Equal(0, await new UnitOfWork(db).SaveAsync()); // Committed events are acknowledged, never written twice.
        }

        await using var read = test.Database.Context();
        var stored = (await new IncomingPaymentRepository(read).FindAsync(" bagage22 ", "E2E-1", default))!;
        Assert.Empty(stored.Payment.PendingEvents);
        Assert.Equal((Participant, "E2E-1", Now, TimeSpan.Zero),
            (stored.Payment.ParticipantBic, stored.Payment.EndToEndId, stored.Payment.RegisteredAtUtc, stored.Payment.RegisteredAtUtc.Offset));
        Assert.Equal(incoming.Payment, stored.Request);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)stored.Request.InitiationChannelInstrument!.InstrumentCodes!)[0] = "changed");
        Assert.Throws<NotSupportedException>(() => ((IList<Pacs008StructuredRemittanceInput>)stored.Request.Remittance!.Structured!).Clear());
        Assert.Single(await Events(read, stored.Payment.Id));
        read.Metadata(stored.Payment).RequestJson = "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => new UnitOfWork(read).SaveAsync());
        await using var tamper = test.Database.Context();
        var payment = (await new IncomingPaymentRepository(tamper).FindAsync(Participant, "E2E-1", default))!.Payment;
        tamper.Metadata(payment).ClaimToken = Guid.NewGuid();
        tamper.Metadata(payment).ClaimExpiresAtUtc = Now.AddHours(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new UnitOfWork(tamper).SaveAsync());
    }

    [Fact]
    public async Task Events_and_state_rows_require_an_identity_of_their_own_kind()
    {
        await using var test = await Harness.CreateAsync();
        var incoming = await test.RegisterNewAsync(1, "E2E-1");
        var outgoing = OutgoingPayment.Receive(Guid.NewGuid(), "pacs.009", "outgoing", Now);
        await using (var db = test.Database.Context())
        {
            new OutgoingPaymentRepository(db).Add(outgoing, "{}", null);
            await new UnitOfWork(db).SaveAsync();
        }

        await using var sql = test.Database.Context();
        var sequence = 10; // Distinct keys, so only ownership can reject these rows.
        Task<int> Event(Guid owner, string kind, string name) => sql.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO TransactionEvents (EventId, TransactionId, AggregateKind, Sequence, Name, SchemaVersion, OccurredAtUtc, PayloadJson)
            VALUES ({Guid.NewGuid()}, {owner}, {kind}, {sequence++}, {name}, 1, {Now}, {"{}"})
            """);
        Assert.Equal(1, await Event(incoming, "incoming-payment", "incoming-payment.test-control"));
        await Assert.ThrowsAsync<SqlException>(() => Event(Guid.NewGuid(), "incoming-payment", "incoming-payment.registered"));
        await Assert.ThrowsAsync<SqlException>(() => Event(outgoing.Id, "incoming-payment", "incoming-payment.registered"));
        await Assert.ThrowsAsync<SqlException>(() => Event(outgoing.Id, "outgoing-payment", "incoming-payment.registered"));
        await Assert.ThrowsAsync<SqlException>(() => Event(incoming, "outgoing-payment", "payment.received"));
        var mismatched = Guid.NewGuid();
        await sql.Database.ExecuteSqlAsync($"INSERT INTO AggregateIdentities (Id, Kind) VALUES ({mismatched}, 'outgoing-payment')");
        await Assert.ThrowsAsync<SqlException>(() => sql.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO IncomingPayments (Id, ParticipantBic, EndToEndId, RegisteredAtUtc, RequestJson, LastSequence)
            VALUES ({mismatched}, 'BAGAGE22', 'E2E-9', {Now}, {"{}"}, 0)
            """));
    }

    [Theory]
    [InlineData("add")]
    [InlineData("modify")]
    [InlineData("delete")]
    public async Task Aggregate_identities_are_append_only_and_written_only_with_new_aggregates(string change)
    {
        await using var test = await Harness.CreateAsync();
        var id = await test.RegisterNewAsync(1, "E2E-1");
        await using var db = test.Database.Context();
        var identityType = db.Model.GetEntityTypes().Single(t => t.GetTableName() == "AggregateIdentities").ClrType;
        if (change == "add")
        {
            var entry = db.Entry(Activator.CreateInstance(identityType)!);
            entry.Property("Id").CurrentValue = Guid.NewGuid();
            entry.Property("Kind").CurrentValue = "incoming-payment";
            entry.State = EntityState.Added;
        }
        else
        {
            var identity = (await db.FindAsync(identityType, id))!;
            // Both identity columns are keys, so EF refuses to stage a change before the save rules are reached.
            if (change == "modify")
            {
                Assert.Throws<InvalidOperationException>(() => db.Entry(identity).Property("Kind").CurrentValue = "outgoing-payment");
            }
            else
            {
                db.Remove(identity);
            }
        }

        if (change != "modify")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new UnitOfWork(db).SaveAsync());
        }

        await using var read = test.Database.Context();
        Assert.Equal(1, await Count(read, "SELECT COUNT(*) AS Value FROM AggregateIdentities WHERE Kind = 'incoming-payment'"));
    }

    [Fact]
    public async Task Protocol_failures_hold_valid_sequences_without_ownership_or_schedule()
    {
        await using var test = await Harness.CreateAsync();
        var held = await test.ReceiveAsync(1);
        var expired = await test.ReceiveAsync(2);
        Assert.True(await test.HoldAsync(held, " Untrusted message signature. "));
        Assert.False(await test.HoldAsync(held, "Held entries have no owner."));
        test.Clock.Now = Now + Lease;
        Assert.False(await test.HoldAsync(expired, "Expired owner."));
        Assert.Equivalent(IncomingRegistration.LostOwnership, await test.RegisterAsync(expired, Incoming()), strict: true);
        await using var read = test.Database.Context();
        var receipt = (await new InboundReceiptRepository(read).ReadAsync(held.JournalId, default))!;
        Assert.Equal(1, receipt.Receipt.Sequence);
        Assert.Equal(InboundProcessingStatus.Held, receipt.Status);
        Assert.Equal("Untrusted message signature.", receipt.HoldReason);
        Assert.Null(receipt.NextActionAtUtc);
        Assert.Equal(new[] { expired.JournalId }, await new InboundWorkRepository(read).FindDueAsync(test.Clock.Now, 10, default));
        Assert.Equal(0, await Count(read, "SELECT COUNT(*) AS Value FROM IncomingPayments"));
    }

    private static IncomingPacs008 Incoming(string endToEndId = "E2E-1", string message = "MSG-1", Func<Pacs008Request, Pacs008Request>? change = null)
    {
        var request = new Pacs008Request
        {
            InstructionId = "INSTRUCTION-1",
            EndToEndId = endToEndId,
            CreationDateTime = Now,
            AcceptanceDateTime = Now,
            Amount = 12.50m,
            Currency = "GEL",
            InstructionPriority = "HIGH",
            Debtor = new() { Type = 1, Name = "Debtor", ParticipantBic = "NBGEGE22", Account = "GE29NB0000000101904917", Address = new() { TownName = "Tbilisi" } },
            Creditor = new() { Type = 0, Name = "Creditor", ParticipantBic = Participant, Account = "GE29BG0000000101904917" },
            PaymentInitiation = new() { ChannelCode = "WEB", Geolocation = [] },
            InitiationChannelInstrument = new() { ChannelCode = "MOB", InstrumentCodes = ["QR", "NFC"] },
            Remittance = new() { Unstructured = "invoice 7", Structured = [new() { ReferenceType = "MCC", Reference = "5411" }] }
        };
        return new(change?.Invoke(request) ?? request,
            new(message, "GROUP-" + message, endToEndId, "TX-1", null, Now, new DateOnly(2026, 10, 4), "NBGEGE22", "INST", "INST"));
    }

    private static IncomingPaymentIntake Intake(TransactionDbContext db, DateTimeOffset now) => new(new IncomingPaymentRepository(db), new InboundWorkRepository(db), new UnitOfWork(db), new Clock { Now = now }, new IncomingProcessingOptions());
    private static IncomingPaymentWork Work(TransactionDbContext db, DateTimeOffset now) => new(new IncomingPaymentWorkRepository(db), new UnitOfWork(db), new Clock { Now = now });
    private static Task<List<string>> Events(TransactionDbContext db, Guid id) => db.Database.SqlQuery<string>($"SELECT Name AS Value FROM TransactionEvents WHERE TransactionId = {id} ORDER BY Sequence").ToListAsync();
    private static Task<int> Count(TransactionDbContext db, string sql) => db.Database.SqlQueryRaw<int>(sql).SingleAsync();
    private static async Task<(Guid? PaymentId, string? BusinessMessageId)> Attachment(TransactionDbContext db, Guid journalId)
    {
        var row = await db.Database.SqlQuery<AttachmentRow>($"""
            SELECT IncomingPaymentId, JSON_VALUE(OriginalJson, '$.value.businessMessageId') AS BusinessMessageId
            FROM InboundMessageJournal WHERE Id = {journalId}
            """).SingleAsync();
        return (row.IncomingPaymentId, row.BusinessMessageId);
    }

    private sealed class Harness(SqlTestDatabase database, ServiceProvider provider, Clock clock) : IAsyncDisposable
    {
        public SqlTestDatabase Database { get; } = database;
        public Clock Clock { get; } = clock;

        public static async Task<Harness> CreateAsync(IInterceptor? interceptor = null)
        {
            var database = await SqlTestDatabase.CreateAsync();
            await using var db = database.Context();
            var clock = new Clock
            {
                Now = Now
            };
            var services = new ServiceCollection().AddSingleton<TimeProvider>(clock)
                .AddPersistence(db.Database.GetConnectionString()!).AddInboundFoundations();
            if (interceptor is not null)
            {
                services.AddDbContext<TransactionDbContext>(builder => builder.AddInterceptors(interceptor));
            }

            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            return new(database, provider, clock);
        }

        public async Task<InboundClaim> ReceiveAsync(long sequence, string participant = Participant)
        {
            var receipt = await provider.GetRequiredService<InboundReceiptRegistration>()
                .RegisterAsync(new(participant, sequence, "pacs.008", "<signed/>", false, Clock.Now), default);
            return (await InScope<InboundWork, InboundClaim?>(work => work.AcquireAsync(receipt.JournalId, Lease, default)))!;
        }

        public Task<IncomingRegistration> RegisterAsync(InboundClaim claim, IncomingPacs008 incoming) => provider.GetRequiredService<IncomingPaymentRegistration>().RegisterAsync(claim, incoming, default);
        public async Task<Guid> RegisterNewAsync(long sequence, string endToEndId) => (await RegisterAsync(await ReceiveAsync(sequence), Incoming(endToEndId))).PaymentId!.Value;
        public Task<bool> HoldAsync(InboundClaim claim, string reason) => InScope<InboundWork, bool>(work => work.HoldAsync(claim, reason, default));
        public Task<IncomingPaymentClaim?> AcquireAsync(Guid paymentId) => InScope<IncomingPaymentWork, IncomingPaymentClaim?>(work => work.AcquireAsync(paymentId, Lease, default));
        public Task<bool> ReleaseAsync(IncomingPaymentClaim claim, DateTimeOffset nextActionAtUtc) => InScope<IncomingPaymentWork, bool>(work => work.ReleaseAsync(claim, nextActionAtUtc, default));
        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
            await Database.DisposeAsync();
        }

        private async Task<T> InScope<TService, T>(Func<TService, Task<T>> run)
            where TService : notnull
        {
            await using var scope = provider.CreateAsyncScope();
            return await run(scope.ServiceProvider.GetRequiredService<TService>());
        }
    }

    private sealed class AttachmentRow
    {
        public Guid? IncomingPaymentId { get; set; }
        public string? BusinessMessageId { get; set; }
    }

    /// <summary>Once armed, holds the first saves until all parties arrive, so their lookups precede every insert.</summary>
    private sealed class SaveBarrier(int parties) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;
        public bool Armed { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Armed)
            {
                return result;
            }

            var arrival = Interlocked.Increment(ref _count);
            if (arrival == parties)
            {
                _arrived.SetResult();
            }

            if (arrival <= parties)
            {
                await _arrived.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return result;
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FailEventSave : SaveChangesInterceptor
    {
        private int _calls;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 2)
            {
                throw new InvalidOperationException("Fail after entity writes, before commit.");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class CancelAfterEntities(CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return ValueTask.FromResult(result);
        }
    }
}
