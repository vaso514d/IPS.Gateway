using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using Xunit;

namespace IPS.Middleware.Tests.Transactions;

public sealed class OutgoingTransactionIntakeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Intake_waits_for_commit_and_acknowledges_durable_acceptance_only_after_it()
    {
        var storage = new IntakeStorage();
        var pending = new OutgoingTransactionIntake(storage, storage, new Clock()).AcceptAsync(ValidatedIntakeRequest.Validate(" pacs.008 ", " CBS-1 ", "{\"a\":1}").Request!, CancellationToken.None);
        Assert.False(pending.IsCompleted);
        Assert.NotNull(storage.Added);
        Assert.Equal(Now, storage.Added.CreatedAtUtc);
        Assert.Equal("CBS-1", storage.Added.ClientReference);
        Assert.Equal("{\"a\":1}", storage.Json);
        storage.Commit.SetResult(2);
        var result = await pending;
        Assert.True(result.Created);
        Assert.Same(storage.Added, result.Payment);
    }

    [Fact]
    public async Task Duplicate_reference_returns_original_without_staging_or_committing()
    {
        var storage = new IntakeStorage { Existing = OutgoingPayment.Receive(Guid.NewGuid(), "pacs.009", "CBS-1", Now) };
        var result = await new OutgoingTransactionIntake(storage, storage, new Clock()).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "CBS-1", "{\"changed\":true}").Request!, CancellationToken.None);
        Assert.False(result.Created);
        Assert.Same(storage.Existing, result.Payment);
        Assert.Null(storage.Added);
        Assert.Equal(0, storage.CommitCalls);
    }

    [Fact]
    public async Task An_insert_race_returns_the_persisted_winner()
    {
        var storage = new IntakeStorage { Winner = OutgoingPayment.Receive(Guid.NewGuid(), "pacs.009", "CBS-1", Now) };
        storage.Commit.SetException(new UniqueConstraintException("Unique key conflict.", new Exception("SQL failure")));
        var result = await new OutgoingTransactionIntake(storage, storage, new Clock()).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "CBS-1", "{}").Request!, CancellationToken.None);
        Assert.False(result.Created);
        Assert.Same(storage.Winner, result.Payment);
    }

    [Fact]
    public async Task Commit_failure_propagates_and_cancelled_intake_stages_nothing()
    {
        var storage = new IntakeStorage();
        storage.Commit.SetException(new InvalidOperationException("unavailable"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OutgoingTransactionIntake(storage, storage, new Clock()).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "CBS-1", "{}").Request!, CancellationToken.None));
        var cancelled = new IntakeStorage();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new OutgoingTransactionIntake(cancelled, cancelled, new Clock()).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "CBS-1", "{}").Request!, cancellation.Token));
        Assert.Null(cancelled.Added);
    }

    [Fact]
    public async Task A_unique_failure_without_a_reference_winner_is_not_duplicate_intake()
    {
        var storage = new IntakeStorage();
        var failure = new UniqueConstraintException("Unrelated unique constraint.", new Exception("SQL failure"));
        storage.Commit.SetException(failure);
        var actual = await Assert.ThrowsAsync<UniqueConstraintException>(() =>
            new OutgoingTransactionIntake(storage, storage, new Clock()).AcceptAsync(ValidatedIntakeRequest.Validate("pacs.008", "CBS-1", "{}").Request!, default));
        Assert.Same(failure, actual);
    }
    [Fact]
    public async Task Cancellation_is_forwarded_to_a_pending_save()
    {
        var storage = new IntakeStorage();
        using var cancellation = new CancellationTokenSource();
        var request = ValidatedIntakeRequest.Validate("pacs.008", "cancel-save", "{}").Request!;
        var pending = new OutgoingTransactionIntake(storage, storage, new Clock()).AcceptAsync(request, cancellation.Token);
        Assert.Equal(1, storage.CommitCalls);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }

    private sealed class IntakeStorage : IOutgoingPaymentRepository, IUnitOfWork
    {
        public OutgoingPayment? Existing { get; init; }
        public OutgoingPayment? Winner { get; init; }
        public OutgoingPayment? Added { get; private set; }
        public string? Json { get; private set; }
        public int CommitCalls { get; private set; }
        public TaskCompletionSource<int> Commit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<OutgoingPayment?> FindByClientReferenceAsync(string reference, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CommitCalls == 0 ? Existing : Winner);
        }
        public void Add(OutgoingPayment payment, string requestJson) { Added = payment; Json = requestJson; }
        public Task<int> SaveAsync(CancellationToken cancellationToken) { CommitCalls++; return Commit.Task.WaitAsync(cancellationToken); }
        public Task<OutgoingPayment?> FindAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ReadRequestAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<StoredPaymentEvent>> ReadEventsAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
