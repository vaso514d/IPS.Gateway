using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Repositories.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Transactions;

public sealed class PaymentSubmissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 14, 0, 0, TimeSpan.Zero);
    private const string Xml = "<Message>საქართველო &amp; test</Message>";
    private const string Signed = "<Message><Signature>fixture</Signature></Message>";
    private static IpsSubmissionResponse Response => new(200, "<reply>  საქართველო\r\n</reply>",
        [new("X-MONTRAN-IPS-ReqSts", "ACCP"), new("X-Test", "one"), new("X-Test", "two")]);

    [Theory]
    [InlineData(SubmissionMessageKind.Signed)]
    [InlineData(SubmissionMessageKind.DevelopmentUnsigned)]
    public async Task Submission_and_response_survive_fresh_context_with_exact_evidence(SubmissionMessageKind kind)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var (payment, claim) = await Start(session, signed: kind == SubmissionMessageKind.Signed);
        var repository = new PaymentSubmissionRepository(session.Context);
        var outcome = payment.Current;
        repository.StageSubmission(payment, claim, kind, Now.ToOffset(TimeSpan.FromHours(4)));
        await using (var before = database.Session())
            Assert.Null((await new PaymentSubmissionRepository(before.Context).ReadAsync(payment.Id, default))!.Marker);
        Assert.Equal(1, await session.Unit.SaveAsync());
        Assert.Throws<InvalidOperationException>(() => repository.StageSubmission(payment, claim, kind, Now));
        var response = Response;
        repository.StageResponse(payment, claim, response, Now.AddSeconds(1));
        await using (var before = database.Session())
            Assert.Null((await new PaymentSubmissionRepository(before.Context).ReadAsync(payment.Id, default))!.Response);
        Assert.Equal(1, await session.Unit.SaveAsync());
        repository.StageResponse(payment, claim, Response, Now.AddSeconds(2));
        Assert.Equal(0, await session.Unit.SaveAsync());
        Assert.Throws<InvalidOperationException>(() => repository.StageResponse(payment, claim, new(500, "changed", []), Now));
        Assert.Equal(outcome, payment.Current);
        Assert.Empty(payment.PendingEvents);
        Assert.Equal(2, (await session.Payments.ReadEventsAsync(payment.Id, default)).Count);

        await using var read = database.Session();
        var stored = (await new PaymentSubmissionRepository(read.Context).ReadAsync(payment.Id, default))!;
        Assert.Equal(new SubmissionMarker(Now, claim.Token, kind), stored.Marker);
        Assert.Equal(TimeSpan.Zero, stored.Marker!.StartedAtUtc.Offset);
        Assert.Equal(response.Body, stored.Response!.Body);
        Assert.Equal(response.HttpStatusCode, stored.Response.HttpStatusCode);
        Assert.Equal(response.Headers, stored.Response.Headers);
        Assert.Null(await new PaymentSubmissionRepository(read.Context).ReadAsync(Guid.NewGuid(), default));
        var prepared = (await new PaymentPreparationRepository(read.Context).ReadAsync(payment.Id, default))!;
        Assert.Equal(kind == SubmissionMessageKind.Signed ? Signed : null, prepared.SignedXml);
        var current = (await read.Payments.FindAsync(payment.Id, default))!;
        var json = read.Context.Entry(current).Property<string>("SubmissionJson").CurrentValue;
        Assert.Contains("\"startedAtUtc\"", json);
        Assert.DoesNotContain("$type", json);
    }

    [Fact]
    public async Task Every_stage_requires_its_preceding_artifact_to_be_committed()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var (payment, claim) = await Start(session, prepare: false);
        var repository = new PaymentSubmissionRepository(session.Context);
        var preparation = new PaymentPreparationRepository(session.Context);
        Assert.Throws<InvalidOperationException>(() => repository.StageSubmission(payment, claim, SubmissionMessageKind.DevelopmentUnsigned, Now));
        preparation.StageUnsignedXml(payment, claim, Xml, Now);
        Assert.Throws<InvalidOperationException>(() => repository.StageSubmission(payment, claim, SubmissionMessageKind.DevelopmentUnsigned, Now));
        await session.Unit.SaveAsync();
        Assert.Throws<InvalidOperationException>(() => repository.StageSubmission(payment, claim, SubmissionMessageKind.Signed, Now));
        preparation.StageSignedXml(payment, claim, Signed, Now);
        Assert.Throws<InvalidOperationException>(() => repository.StageSubmission(payment, claim, SubmissionMessageKind.Signed, Now));
        await session.Unit.SaveAsync();
        Assert.Throws<InvalidOperationException>(() => repository.StageSubmission(payment, claim, SubmissionMessageKind.DevelopmentUnsigned, Now));
        Assert.Throws<InvalidOperationException>(() => repository.StageResponse(payment, claim, Response, Now));
        repository.StageSubmission(payment, claim, SubmissionMessageKind.Signed, Now);
        Assert.Throws<InvalidOperationException>(() => repository.StageSubmission(payment, claim, SubmissionMessageKind.Signed, Now));
        Assert.Throws<InvalidOperationException>(() => repository.StageResponse(payment, claim, Response, Now));
        await session.Unit.SaveAsync();
        repository.StageResponse(payment, claim, Response, Now);
        await session.Unit.SaveAsync();
    }

    [Fact]
    public async Task Development_submission_freezes_preparation_and_does_not_fill_the_signed_slot()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var (payment, claim) = await Start(session, signed: false);
        new PaymentSubmissionRepository(session.Context).StageSubmission(payment, claim, SubmissionMessageKind.DevelopmentUnsigned, Now);
        Assert.Throws<InvalidOperationException>(() => new PaymentPreparationRepository(session.Context).StageSignedXml(payment, claim, Signed, Now));
        await session.Unit.SaveAsync();
        Assert.Throws<InvalidOperationException>(() => new PaymentPreparationRepository(session.Context).StageSignedXml(payment, claim, Signed, Now));
    }

    [Theory]
    [InlineData(false, "expired")]
    [InlineData(true, "expired")]
    [InlineData(false, "wrong-token")]
    [InlineData(true, "wrong-token")]
    [InlineData(false, "wrong-payment")]
    [InlineData(true, "wrong-payment")]
    public async Task Staging_requires_the_current_live_claim(bool response, string invalid)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var (payment, claim) = await Start(session);
        var repository = new PaymentSubmissionRepository(session.Context);
        if (response)
        {
            repository.StageSubmission(payment, claim, SubmissionMessageKind.Signed, Now);
            await session.Unit.SaveAsync();
        }
        var now = invalid == "expired" ? claim.ExpiresAtUtc : Now;
        if (invalid == "wrong-token") claim = claim with { Token = Guid.NewGuid() };
        if (invalid == "wrong-payment") claim = claim with { TransactionId = Guid.NewGuid() };
        Assert.Throws<PersistenceConcurrencyException>(() =>
        {
            if (response) repository.StageResponse(payment, claim, Response, now);
            else repository.StageSubmission(payment, claim, SubmissionMessageKind.Signed, now);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Competing_checkpoint_writers_have_one_winner(bool response)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var first = database.Session();
        var (payment, claim) = await Start(first);
        if (response)
        {
            new PaymentSubmissionRepository(first.Context).StageSubmission(payment, claim, SubmissionMessageKind.Signed, Now);
            await first.Unit.SaveAsync();
        }
        await using var second = database.Session();
        var concurrent = (await second.Payments.FindAsync(payment.Id, default))!;
        if (response)
        {
            new PaymentSubmissionRepository(first.Context).StageResponse(payment, claim, Response, Now);
            new PaymentSubmissionRepository(second.Context).StageResponse(concurrent, claim, new(500, "loser", []), Now);
        }
        else
        {
            new PaymentSubmissionRepository(first.Context).StageSubmission(payment, claim, SubmissionMessageKind.Signed, Now);
            new PaymentSubmissionRepository(second.Context).StageSubmission(concurrent, claim, SubmissionMessageKind.Signed, Now.AddSeconds(1));
        }
        await first.Unit.SaveAsync();
        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() => second.Unit.SaveAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.Unit.SaveAsync());
        await using var read = database.Session();
        var stored = (await new PaymentSubmissionRepository(read.Context).ReadAsync(payment.Id, default))!;
        Assert.Equal(Now, stored.Marker!.StartedAtUtc);
        Assert.Equal(response ? Response.Body : null, stored.Response?.Body);
    }

    [Fact]
    public async Task Recovery_fences_a_late_response_and_the_submission_marker_remains()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var stale = database.Session();
        var (payment, claim) = await Start(stale);
        var repository = new PaymentSubmissionRepository(stale.Context);
        repository.StageSubmission(payment, claim, SubmissionMessageKind.Signed, Now);
        await stale.Unit.SaveAsync();
        repository.StageResponse(payment, claim, Response, Now.AddSeconds(1));
        await using (var recovery = database.Session())
            Assert.Equal(TransactionWorkResult.Saved, await recovery.Processing(claim.ExpiresAtUtc).TryRecoverAsync(payment.Id, default));
        await Assert.ThrowsAsync<PersistenceConcurrencyException>(() => stale.Unit.SaveAsync());
        await using var read = database.Session();
        var stored = (await new PaymentSubmissionRepository(read.Context).ReadAsync(payment.Id, default))!;
        Assert.NotNull(stored.Marker);
        Assert.Null(stored.Response);
        Assert.Equal(TransactionStatus.Uncertain, (await read.Payments.FindAsync(payment.Id, default))!.CurrentStatus);
    }

    [Fact]
    public async Task A_new_owner_cannot_attribute_a_response_to_the_old_submission()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var (payment, claim) = await Start(session);
        var repository = new PaymentSubmissionRepository(session.Context);
        repository.StageSubmission(payment, claim, SubmissionMessageKind.Signed, Now);
        await session.Unit.SaveAsync();
        Assert.True(session.Work.StageRecovery(payment, claim.ExpiresAtUtc));
        var replacement = session.Work.StageClaim(payment, claim.ExpiresAtUtc, TimeSpan.FromSeconds(45))!;
        await session.Unit.SaveAsync();
        Assert.Throws<PersistenceConcurrencyException>(() => repository.StageResponse(payment, replacement, Response, claim.ExpiresAtUtc));
    }

    [Fact]
    public async Task Response_outcome_events_and_ownership_can_commit_atomically()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var (payment, claim) = await Start(session);
        var repository = new PaymentSubmissionRepository(session.Context);
        repository.StageSubmission(payment, claim, SubmissionMessageKind.Signed, Now);
        await session.Unit.SaveAsync();
        repository.StageResponse(payment, claim, Response, Now);
        payment.RecordAcceptance(StatusSource.Ips, Now.AddSeconds(1));
        Assert.True(session.Work.StageCompletion(payment, claim, Now, null));
        await session.Unit.SaveAsync();
        await using var read = database.Session();
        Assert.NotNull((await new PaymentSubmissionRepository(read.Context).ReadAsync(payment.Id, default))!.Response);
        var stored = (await read.Payments.FindAsync(payment.Id, default))!;
        Assert.Equal(TransactionStatus.Accepted, stored.CurrentStatus);
        Assert.Equal(3, (await read.Payments.ReadEventsAsync(payment.Id, default)).Count);
        Assert.Null(read.Context.Entry(stored).Property<Guid?>("ClaimToken").CurrentValue);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Failed_commit_rolls_back_checkpoint_state_and_release_without_losing_events(bool response, bool cancel)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        Guid id;
        TransactionClaim claim;
        await using (var setup = database.Session())
        {
            var started = await Start(setup);
            id = started.Payment.Id;
            claim = started.Claim;
            if (response)
            {
                new PaymentSubmissionRepository(setup.Context).StageSubmission(started.Payment, claim, SubmissionMessageKind.Signed, Now);
                await setup.Unit.SaveAsync();
            }
        }
        using var cancellation = new CancellationTokenSource();
        await using var session = cancel ? database.Session(new CancelAfterParent(cancellation)) : database.Session();
        var payment = (await session.Payments.FindAsync(id, default))!;
        if (!cancel)
            await session.Context.Database.ExecuteSqlRawAsync("ALTER TABLE TransactionEvents ADD CONSTRAINT CK_Test_Response CHECK (Sequence <= 2)");
        var repository = new PaymentSubmissionRepository(session.Context);
        if (response)
        {
            repository.StageResponse(payment, claim, Response, Now);
            payment.RecordAcceptance(StatusSource.Ips, Now);
        }
        else
        {
            repository.StageSubmission(payment, claim, SubmissionMessageKind.Signed, Now);
            payment.RecordStep(ProcessingStep.Sent, Now);
        }
        session.Work.StageCompletion(payment, claim, Now, null);
        var eventId = Assert.Single(payment.PendingEvents).EventId;
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Unit.SaveAsync(cancellation.Token));
        else await Assert.ThrowsAsync<DbUpdateException>(() => session.Unit.SaveAsync());
        Assert.Equal(eventId, Assert.Single(payment.PendingEvents).EventId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Unit.SaveAsync());
        await using var read = database.Session();
        var checkpoint = (await new PaymentSubmissionRepository(read.Context).ReadAsync(id, default))!;
        Assert.Null(checkpoint.Response);
        Assert.Equal(response, checkpoint.Marker is not null);
        var stored = (await read.Payments.FindAsync(id, default))!;
        Assert.Equal(TransactionStatus.Sending, stored.CurrentStatus);
        Assert.Equal(claim.Token, read.Context.Entry(stored).Property<Guid?>("ClaimToken").CurrentValue);
    }

    [Theory]
    [InlineData("SubmissionJson", "insert")]
    [InlineData("SubmissionJson", "alter-authorized")]
    [InlineData("SubmissionJson", "replace")]
    [InlineData("SubmissionJson", "clear")]
    [InlineData("SubmissionResponseJson", "insert")]
    [InlineData("SubmissionResponseJson", "alter-authorized")]
    [InlineData("SubmissionResponseJson", "replace")]
    [InlineData("SubmissionResponseJson", "clear")]
    public async Task Direct_EF_cannot_bypass_immutable_authorized_checkpoints(string column, string edit)
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var (payment, claim) = await Start(session);
        var repository = new PaymentSubmissionRepository(session.Context);
        if (column == "SubmissionResponseJson" || edit != "insert")
        {
            repository.StageSubmission(payment, claim, SubmissionMessageKind.Signed, Now);
            if (column == "SubmissionResponseJson" || edit != "alter-authorized") await session.Unit.SaveAsync();
        }
        if (column == "SubmissionResponseJson" && edit != "insert")
        {
            repository.StageResponse(payment, claim, Response, Now);
            if (edit != "alter-authorized") await session.Unit.SaveAsync();
        }
        // Even an authorized ownership operation is not permission to write arbitrary checkpoint data.
        session.Work.StageCompletion(payment, claim, Now, null);
        session.Context.Entry(payment).Property<string?>(column).CurrentValue = edit == "clear" ? null : "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.Unit.SaveAsync());
    }

    [Fact]
    public async Task Unstarted_other_type_and_missing_payments_do_not_offer_submission()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var repository = new PaymentSubmissionRepository(session.Context);
        var payment = (await session.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "waiting", "{}").Request!, default)).Payment;
        var claim = new TransactionClaim(payment.Id, Guid.NewGuid(), Now.AddSeconds(45));
        Assert.Equal(new PaymentSubmission(null, null), await repository.ReadAsync(payment.Id, default));
        Assert.Throws<InvalidOperationException>(() => repository.StageSubmission(payment, claim, SubmissionMessageKind.Signed, Now));
        var other = (await session.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.009", "other", "{}").Request!, default)).Payment;
        Assert.Null(await repository.ReadAsync(other.Id, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => repository.StageSubmission(payment, claim, (SubmissionMessageKind)99, Now));
    }

    private static async Task<(OutgoingPayment Payment, TransactionClaim Claim)> Start(PaymentSession session, bool signed = true, bool prepare = true)
    {
        var payment = (await session.Intake(Now).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "submission", "{}").Request!, default)).Payment;
        var claim = (await session.Processing(Now).TryStartAsync(payment.Id, TimeSpan.FromSeconds(45), default))!;
        if (prepare)
        {
            var repository = new PaymentPreparationRepository(session.Context);
            repository.StageUnsignedXml(payment, claim, Xml, Now);
            await session.Unit.SaveAsync();
            if (signed)
            {
                repository.StageSignedXml(payment, claim, Signed, Now);
                await session.Unit.SaveAsync();
            }
        }
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
