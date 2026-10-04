using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Repositories.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Transactions;

public sealed class PaymentPreparationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 14, 0, 0, TimeSpan.Zero);
    private const string Xml = "<Message>\r\n  <Name>საქართველო &amp; test</Name>\n</Message>";
    private const string Signed = "<Message>\r\n  <Name>საქართველო &amp; test</Name><Signature>fixture</Signature>\n</Message>";

    [Fact]
    public async Task Concurrent_duplicate_intake_keeps_one_pair_of_identifiers_and_original_request()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var session = database.Session();
            return await session.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "duplicate", "{\"original\":true}").Request!, default);
        }));
        var original = Assert.Single(results.Where(r => r.Created));
        Assert.Single(results.Select(r => r.Payment.Id).Distinct());
        await using var read = database.Session();
        var repository = new PaymentPreparationRepository(read.Context);
        var identifiers = Assert.IsType<IPS.Middleware.Application.Payments.Pacs008.PreparedPaymentMessage>(
            await repository.ReadAsync(original.Payment.Id, default));
        Assert.Matches("^[0-9a-f]{32}$", identifiers.MessageId);
        Assert.Matches("^[0-9a-f]{32}$", identifiers.TransactionId);
        Assert.NotEqual(identifiers.MessageId, identifiers.TransactionId);
        Assert.Null(identifiers.UnsignedXml);
        Assert.Null(identifiers.SignedXml);
        var duplicate = await read.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", " duplicate ", "{\"replacement\":true}").Request!, default);
        Assert.False(duplicate.Created);
        Assert.Equal(identifiers, await repository.ReadAsync(duplicate.Payment.Id, default));
        Assert.Equal("{\"original\":true}", await read.Payments.ReadRequestAsync(duplicate.Payment.Id, default));
        var another = await read.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "another", "{}").Request!, default);
        var other = (await repository.ReadAsync(another.Payment.Id, default))!;
        Assert.NotEqual(identifiers.MessageId, other.MessageId);
        Assert.NotEqual(identifiers.TransactionId, other.TransactionId);
        var transfer = await read.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.009", "transfer", "{}").Request!, default);
        Assert.Null(await repository.ReadAsync(transfer.Payment.Id, default));
        Assert.Null(await repository.ReadAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Artifacts_round_trip_exactly_without_changing_business_outcome_or_events()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var (payment, claim) = await Start(session);
        var repository = new PaymentPreparationRepository(session.Context);
        var before = payment.Current;
        var sequence = payment.EventSequence;
        repository.StageUnsignedXml(payment, claim, Xml, Now);
        await using (var beforeCommit = database.Session())
            Assert.Null((await new PaymentPreparationRepository(beforeCommit.Context).ReadAsync(payment.Id, default))!.UnsignedXml);
        Assert.Equal(1, await session.Unit.SaveAsync());
        repository.StageUnsignedXml(payment, claim, Xml, Now);
        Assert.Equal(0, await session.Unit.SaveAsync());
        Assert.Throws<InvalidOperationException>(() => repository.StageUnsignedXml(payment, claim, Xml + " ", Now));
        repository.StageSignedXml(payment, claim, Signed, Now);
        Assert.Equal(1, await session.Unit.SaveAsync());
        repository.StageSignedXml(payment, claim, Signed, Now);
        Assert.Equal(0, await session.Unit.SaveAsync());
        Assert.Throws<InvalidOperationException>(() => repository.StageSignedXml(payment, claim, Signed + " ", Now));

        await using var read = database.Session();
        var stored = (await new PaymentPreparationRepository(read.Context).ReadAsync(payment.Id, default))!;
        Assert.Equal(Xml, stored.UnsignedXml);
        Assert.Equal(Signed, stored.SignedXml);
        var current = (await read.Payments.FindAsync(payment.Id, default))!;
        Assert.Equal(before, current.Current);
        Assert.Equal(sequence, current.EventSequence);
        Assert.Equal(claim.Token, read.Context.Entry(current).Property<Guid?>("ClaimToken").CurrentValue);
        Assert.Equal(claim.ExpiresAtUtc, read.Context.Entry(current).Property<DateTimeOffset?>("ClaimExpiresAtUtc").CurrentValue);
        Assert.Equal(2, (await read.Payments.ReadEventsAsync(payment.Id, default)).Count);
    }

    [Fact]
    public async Task Signed_artifact_requires_a_previously_committed_unsigned_artifact()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var (payment, claim) = await Start(session);
        var repository = new PaymentPreparationRepository(session.Context);
        Assert.Throws<InvalidOperationException>(() => repository.StageSignedXml(payment, claim, Signed, Now));
        repository.StageUnsignedXml(payment, claim, Xml, Now);
        Assert.Throws<InvalidOperationException>(() => repository.StageSignedXml(payment, claim, Signed, Now));
        await session.Unit.SaveAsync();
        repository.StageSignedXml(payment, claim, Signed, Now);
        await session.Unit.SaveAsync();
    }

    [Theory]
    [InlineData("wrong-token")]
    [InlineData("wrong-payment")]
    [InlineData("expired")]
    [InlineData("no-claim")]
    public async Task Artifact_staging_rejects_invalid_ownership(string situation)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var (payment, claim) = await Start(session);
        var now = Now;
        if (situation == "wrong-token") claim = claim with { Token = Guid.NewGuid() };
        if (situation == "wrong-payment") claim = claim with { TransactionId = Guid.NewGuid() };
        if (situation == "expired") now = claim.ExpiresAtUtc;
        if (situation == "no-claim")
        {
            Assert.True(session.Work.StageCompletion(payment, claim, Now, null));
            await session.Unit.SaveAsync();
        }
        var repository = new PaymentPreparationRepository(session.Context);
        Assert.Throws<PersistenceConcurrencyException>(() => repository.StageUnsignedXml(payment, claim, Xml, now));
        Assert.Null((await repository.ReadAsync(payment.Id, default))!.UnsignedXml);
    }

    [Fact]
    public async Task Preparation_rejects_empty_content_wrong_state_other_message_type_and_unpersisted_payments()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var (payment, claim) = await Start(session);
        var repository = new PaymentPreparationRepository(session.Context);
        Assert.Throws<ArgumentException>(() => repository.StageUnsignedXml(payment, claim, " ", Now));
        var received = (await session.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "received", "{}").Request!, default)).Payment;
        Assert.Throws<InvalidOperationException>(() => repository.StageUnsignedXml(received, claim, Xml, Now));
        var other = (await session.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.009", "other", "{}").Request!, default)).Payment;
        var otherClaim = (await session.Processing(Now).TryStartAsync(other.Id, TimeSpan.FromSeconds(45), default))!;
        Assert.Throws<InvalidOperationException>(() => repository.StageUnsignedXml(other, otherClaim, Xml, Now));
        var unsaved = OutgoingPayment.Receive(Guid.NewGuid(), "pacs.008", "unsaved", Now);
        session.Payments.Add(unsaved, "{}", accepted: null);
        Assert.Throws<InvalidOperationException>(() => repository.StageUnsignedXml(unsaved, claim, Xml, Now));
        var detached = OutgoingPayment.Receive(Guid.NewGuid(), "pacs.008", "detached", Now);
        Assert.Throws<InvalidOperationException>(() => repository.StageUnsignedXml(detached, claim, Xml, Now));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_concurrent_writer_or_recovery_fences_the_old_artifact_save(bool recover)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var stale = database.Session();
        var (payment, claim) = await Start(stale);
        new PaymentPreparationRepository(stale.Context).StageUnsignedXml(payment, claim, Xml, Now);
        await using (var winner = database.Session())
        {
            var current = (await winner.Payments.FindAsync(payment.Id, default))!;
            if (recover)
            {
                Assert.True(winner.Work.StageRecovery(current, claim.ExpiresAtUtc));
                current.MarkOutcomeUnknown(StatusSource.Recovery, claim.ExpiresAtUtc);
            }
            else
                new PaymentPreparationRepository(winner.Context).StageUnsignedXml(current, claim, "<winner/>", Now);
            await winner.Unit.SaveAsync();
        }
        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() => stale.Unit.SaveAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => stale.Unit.SaveAsync());
        await using var read = database.Session();
        var stored = (await new PaymentPreparationRepository(read.Context).ReadAsync(payment.Id, default))!;
        Assert.Equal(recover ? null : "<winner/>", stored.UnsignedXml);
        var currentPayment = (await read.Payments.FindAsync(payment.Id, default))!;
        Assert.Equal(recover ? TransactionStatus.Uncertain : TransactionStatus.Sending, currentPayment.CurrentStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Event_failure_or_cancellation_rolls_back_artifact_and_retains_pending_event(bool cancel)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        Guid id;
        TransactionClaim claim;
        await using (var intake = database.Session())
        {
            var started = await Start(intake);
            id = started.Payment.Id;
            claim = started.Claim;
        }
        using var cancellation = new CancellationTokenSource();
        await using var session = cancel ? database.Session(new CancelAfterParent(cancellation)) : database.Session();
        var payment = (await session.Payments.FindAsync(id, default))!;
        var repository = new PaymentPreparationRepository(session.Context);
        if (!cancel)
            await session.Context.Database.ExecuteSqlRawAsync("ALTER TABLE TransactionEvents ADD CONSTRAINT CK_Test_Preparation CHECK (Sequence <= 2)");
        repository.StageUnsignedXml(payment, claim, Xml, Now);
        payment.RecordStep(ProcessingStep.XmlGenerated, Now);
        var eventId = Assert.Single(payment.PendingEvents).EventId;
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Unit.SaveAsync(cancellation.Token));
        else
            await Assert.ThrowsAsync<DbUpdateException>(() => session.Unit.SaveAsync());
        Assert.Equal(eventId, Assert.Single(payment.PendingEvents).EventId);
        await using var read = database.Session();
        Assert.Null((await new PaymentPreparationRepository(read.Context).ReadAsync(id, default))!.UnsignedXml);
        Assert.Equal(2, (await read.Payments.ReadEventsAsync(id, default)).Count);
    }

    [Theory]
    [InlineData("MessageId")]
    [InlineData("ProtocolTransactionId")]
    [InlineData("UnsignedXml")]
    [InlineData("SignedXml")]
    public async Task Direct_EF_replacement_or_clearing_is_rejected(string property)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        Guid id;
        await using (var session = database.Session())
        {
            var (payment, claim) = await Start(session);
            id = payment.Id;
            var repository = new PaymentPreparationRepository(session.Context);
            repository.StageUnsignedXml(payment, claim, Xml, Now);
            await session.Unit.SaveAsync();
            repository.StageSignedXml(payment, claim, Signed, Now);
            await session.Unit.SaveAsync();
        }
        foreach (var replacement in new string?[] { null, "changed" })
        {
            await using var tamper = database.Session();
            var payment = (await tamper.Payments.FindAsync(id, default))!;
            tamper.Context.Entry(payment).Property<string?>(property).CurrentValue = replacement;
            await Assert.ThrowsAsync<InvalidOperationException>(() => tamper.Unit.SaveAsync());
        }
    }

    [Fact]
    public async Task Ownership_authorization_alone_does_not_allow_direct_artifact_insertion()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var payment = (await session.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "tamper", "{}").Request!, default)).Payment;
        Assert.NotNull(session.Work.StageClaim(payment, Now, TimeSpan.FromSeconds(45)));
        payment.BeginSending(Now);
        session.Context.Entry(payment).Property<string?>("UnsignedXml").CurrentValue = Xml;
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Unit.SaveAsync());
    }

    [Fact]
    public async Task Domain_property_changes_still_require_events()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var payment = (await session.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "domain-tamper", "{}").Request!, default)).Payment;
        session.Context.Entry(payment).Property(p => p.CurrentSource).CurrentValue = StatusSource.Operator;
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Unit.SaveAsync());
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(false, "<replacement/>")]
    [InlineData(true, "")]
    [InlineData(true, "<replacement/>")]
    public async Task Direct_EF_edits_cannot_replace_the_authorized_first_value(bool signed, string replacement)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var (payment, claim) = await Start(session);
        var repository = new PaymentPreparationRepository(session.Context);
        repository.StageUnsignedXml(payment, claim, Xml, Now);
        if (signed)
        {
            await session.Unit.SaveAsync();
            repository.StageSignedXml(payment, claim, Signed, Now);
        }
        session.Context.Entry(payment).Property<string?>(signed ? "SignedXml" : "UnsignedXml").CurrentValue = replacement;
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Unit.SaveAsync());
        await using var read = database.Session();
        var stored = (await new PaymentPreparationRepository(read.Context).ReadAsync(payment.Id, default))!;
        Assert.Equal(signed ? Xml : null, stored.UnsignedXml);
        Assert.Null(stored.SignedXml);
    }

    private static async Task<(OutgoingPayment Payment, TransactionClaim Claim)> Start(PaymentSession session)
    {
        var payment = (await session.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "preparation", "{}").Request!, default)).Payment;
        var claim = await session.Processing(Now).TryStartAsync(payment.Id, TimeSpan.FromSeconds(45), default);
        Assert.NotNull(claim);
        return (payment, claim);
    }

    private sealed class CancelAfterParent(CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return ValueTask.FromResult(result);
        }
    }
}
