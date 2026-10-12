using System.Data.Common;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Repositories.Payments;
using IPS.Middleware.IntegrationTests.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static IPS.Middleware.IntegrationTests.Payments.ProcessingHarness;

namespace IPS.Middleware.IntegrationTests.Payments;

public sealed class OutgoingJournalTests
{
    [Theory]
    [InlineData("ACCP", MessageJournalStatus.Processed, TransactionStatus.Accepted)]
    [InlineData("RJCT", MessageJournalStatus.Processed, TransactionStatus.Rejected)]
    [InlineData("invalid", MessageJournalStatus.Failed, TransactionStatus.Uncertain)]
    public async Task Interpretation_preserves_independent_technical_and_business_states(
        string status, MessageJournalStatus expected, TransactionStatus outcome)
    {
        await using var harness = await CreateAsync();
        harness.Ips.Behavior = (reply, _) => status == "invalid" ? Task.FromResult(new IpsSubmissionResponse(503, "<broken", [new("X-Test", "one")]))
            : harness.Ips.RespondAsync(reply with { GroupStatus = status, TransactionStatus = status }, status);
        var id = await harness.AcceptAsync();
        Assert.Equal(outcome, (await harness.ProcessAsync(id))!.Status);
        await using var session = harness.Database.Session();
        var rows = await session.Submissions.ReadJournalAsync(id, default);
        Assert.Equal(2, rows.Count);
        Assert.Equal(MessageJournalStatus.SendStarted, rows[0].Status);
        Assert.Equal(expected, rows[1].Status);
        Assert.Equal(rows[0].Id, rows[1].OriginatingMessageId);
        Assert.Equal(rows[0].PaymentId, rows[1].PaymentId);
        Assert.Equal(Assert.Single(harness.Ips.Received), rows[0].Content);
        Assert.Equal(rows[1].Content, rows[1].Response!.Body);
        Assert.NotNull(rows[1].ProcessedAtUtc);
        Assert.Equal(status == "invalid", rows[1].Failure is not null);
        Assert.Equal(status == "invalid" ? null : "pacs.002.001.14", rows[1].MessageDefinition);
        Assert.All(rows, row => Assert.Equal(TimeSpan.Zero, row.CreatedAtUtc.Offset));
    }

    [Fact]
    public async Task Failed_interpretation_commit_preserves_received_evidence_and_no_final_event()
    {
        await using var harness = await CreateAsync();
        var id = await harness.AcceptAsync();
        await Assert.ThrowsAsync<SimulatedCrash>(() => harness.ProcessAsync(id, default,
            new FailAfterInterpretationWrite()));
        await using (var read = harness.Database.Session())
        {
            Assert.Equal(MessageJournalStatus.Received, (await read.Submissions.ReadJournalAsync(id, default))[1].Status);
            Assert.Equal(TransactionStatus.Sending, (await read.Payments.FindAsync(id, default))!.CurrentStatus);
            Assert.DoesNotContain(await read.Payments.ReadEventsAsync(id, default), e => e.Name == "payment.accepted");
        }
        harness.Clock.Now += Ownership;
        await harness.RecoverAsync(id);
        Assert.Equal(TransactionStatus.Accepted, (await harness.ProcessAsync(id))!.Status);
        Assert.Single(harness.Ips.Received);
    }

    // The 013a baseline: with TOP, SQL Server scanned the whole journal for a payment's one message once it had grown.
    [Fact]
    public async Task Processing_reads_the_journal_without_top()
    {
        await using var harness = await CreateAsync();
        var id = await harness.AcceptAsync();
        var commands = new JournalReads();
        Assert.Equal(TransactionStatus.Accepted, (await harness.ProcessAsync(id, default, commands))!.Status);
        Assert.NotEmpty(commands.Texts);
        Assert.All(commands.Texts, text => Assert.DoesNotContain("TOP", text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Development_unsigned_ready_message_is_frozen_and_recovered_exactly()
    {
        await using var harness = await CreateAsync(allowUnsignedInDevelopment: true);
        harness.Certificates.Current = null;
        var id = await harness.AcceptAsync();
        await Assert.ThrowsAsync<SimulatedCrash>(() => harness.ProcessAsync(id, default,
            new CrashOnSave(entry => entry.Context.ChangeTracker.Entries<OutgoingMessageRow>()
                .Any(p => p.Entity.Status == MessageJournalStatus.SendStarted && p.Property(r => r.Status).IsModified))));
        string frozen;
        await using (var read = harness.Database.Session())
        {
            var ready = Assert.Single(await read.Submissions.ReadJournalAsync(id, default));
            Assert.Equal(MessageJournalStatus.ReadyToSend, ready.Status);
            Assert.Equal(SubmissionMessageKind.DevelopmentUnsigned, ready.Disposition);
            frozen = ready.Content;
        }
        harness.Clock.Now += Ownership;
        await harness.RecoverAsync(id);
        // A newly available certificate must not silently replace a frozen development artifact.
        harness.Certificates.Current = harness.SigningCertificate;
        Assert.Equal(TransactionStatus.Sending, (await harness.ProcessAsync(id))!.Status);
        Assert.Empty(harness.Ips.Received);
        harness.Certificates.Current = null;
        harness.Clock.Now += TimeSpan.FromSeconds(1);
        Assert.Equal(TransactionStatus.Accepted, (await harness.ProcessAsync(id))!.Status);
        Assert.Equal(frozen, Assert.Single(harness.Ips.Received));
    }

    [Fact]
    public async Task Preparation_requires_a_committed_claim()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var (payment, claim) = await Ready(session);
        await using var fresh = database.Session();
        var other = (await fresh.Intake(Start).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "uncommitted", "{}").Request!, default)).Payment;
        var uncommitted = fresh.Work.StageClaim(other, Start, Ownership)!;
        other.BeginSending(Start);
        Assert.Throws<PersistenceConcurrencyException>(() => new PaymentPreparationRepository(fresh.Context)
            .StageUnsignedXml(other, uncommitted, "<payment/>", Start));
    }

    [Fact]
    public async Task SQL_enforces_one_initial_message_and_same_payment_response_correlation()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using var session = database.Session();
        var (payment, _) = await Ready(session);
        var outbound = session.Context.Set<OutgoingMessageRow>().Local.Single();
        var duplicate = Guid.NewGuid();
        var unique = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => session.Context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO OutgoingMessages (Id,PaymentId,Direction,MessageDefinition,Content,CreatedAtUtc,Status,Disposition)
            VALUES ({duplicate},{payment.Id},0,'pacs.008.001.12','<duplicate/>',{Start},0,0)
            """));
        Assert.Contains(unique.Number, new[] { 2601, 2627 });
        var other = (await session.Intake(Start).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "other", "{}").Request!, default)).Payment;
        var responseId = Guid.NewGuid();
        var correlation = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => session.Context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO OutgoingMessages (Id,PaymentId,Direction,Content,CreatedAtUtc,OriginatingMessageId,Status,HttpStatusCode,HeadersJson)
            VALUES ({responseId},{other.Id},1,'<reply/>',{Start},{outbound.Id},2,200,'[]')
            """));
        Assert.Equal(547, correlation.Number);
        var missingDisposition = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => session.Context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO OutgoingMessages (Id,PaymentId,Direction,MessageDefinition,Content,CreatedAtUtc,Status)
            VALUES ({Guid.NewGuid()},{other.Id},0,'pacs.008.001.12','<payment/>',{Start},0)
            """));
        Assert.Equal(547, missingDisposition.Number);
    }

    private static async Task<(OutgoingPayment, TransactionClaim)> Ready(PaymentSession session)
    {
        var payment = (await session.Intake(Start).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "journal", "{}").Request!, default)).Payment;
        var claim = (await session.Processing(Start).TryStartAsync(payment.Id, Ownership, default))!;
        var preparation = new PaymentPreparationRepository(session.Context);
        preparation.StageUnsignedXml(payment, claim, "<payment/>", Start);
        await session.Unit.SaveAsync();
        preparation.StageSignedXml(payment, claim, "<signed/>", Start);
        await session.Unit.SaveAsync();
        return (payment, claim);
    }

    private sealed class FailAfterInterpretationWrite : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<OutgoingMessageRow>().Any(e => e.Entity.Status == MessageJournalStatus.Processed))
            {
                throw new SimulatedCrash();
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class JournalReads : DbCommandInterceptor
    {
        public List<string> Texts { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        private void Record(DbCommand command)
        {
            if (command.CommandText.StartsWith("SELECT", StringComparison.Ordinal) && command.CommandText.Contains("FROM [OutgoingMessages]", StringComparison.Ordinal))
            {
                Texts.Add(command.CommandText);
            }
        }
    }
}
