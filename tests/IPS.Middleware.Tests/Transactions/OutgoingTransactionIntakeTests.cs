using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using Xunit;

namespace IPS.Middleware.Tests.Transactions;

public sealed class OutgoingTransactionIntakeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Acceptance_waits_for_storage_and_uses_the_supplied_clock()
    {
        var store = new PendingStore();
        var intake = new OutgoingTransactionIntake(store, new FixedTime());
        using var cancellation = new CancellationTokenSource();
        var acceptance = intake.AcceptAsync(" pacs.008 ", " CBS-1 ", "{\"amount\":10}", cancellation.Token);

        Assert.False(acceptance.IsCompleted);
        Assert.NotNull(store.Received);
        Assert.Equal("pacs.008", store.Received.MessageType);
        Assert.Equal("CBS-1", store.Received.ClientReference);
        Assert.Equal(Now, store.Received.CreatedAtUtc);
        Assert.Equal(TransactionDirection.Outgoing, store.Received.Direction);
        Assert.Equal(TransactionStatus.Received, store.Received.Current.Status);
        Assert.NotEqual(Guid.Empty, store.Received.Id);
        Assert.Equal("{\"amount\":10}", store.Payload);
        Assert.Equal(cancellation.Token, store.CancellationToken);

        var committed = new TransactionIntakeResult(store.Received, Created: true);
        store.Completion.SetResult(committed);
        Assert.Same(committed, await acceptance);
    }

    [Fact]
    public async Task Duplicate_acceptance_returns_the_stored_outcome_unchanged()
    {
        var existing = new PaymentTransaction(Guid.NewGuid(), "pacs.009", TransactionDirection.Outgoing, Now, "CBS-1");
        existing.ChangeStatus(TransactionStatus.Accepted, StatusSource.Ips, Now.AddSeconds(1));
        var stored = new TransactionIntakeResult(existing, Created: false);
        var store = new PendingStore();
        store.Completion.SetResult(stored);

        var result = await new OutgoingTransactionIntake(store, new FixedTime())
            .AcceptAsync("pacs.008", "CBS-1", "{}", CancellationToken.None);

        Assert.Same(stored, result);
        Assert.Equal(TransactionStatus.Accepted, result.Transaction.Current.Status);
        Assert.Equal("pacs.009", result.Transaction.MessageType);
    }

    [Fact]
    public async Task Storage_failure_is_not_reported_as_acceptance()
    {
        var store = new PendingStore();
        var failure = new IOException("Storage unavailable");
        store.Completion.SetException(failure);

        var actual = await Assert.ThrowsAsync<IOException>(() =>
            new OutgoingTransactionIntake(store, new FixedTime())
                .AcceptAsync("pacs.008", "CBS-1", "{}", CancellationToken.None));

        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task Cancelled_intake_does_not_call_storage()
    {
        var store = new PendingStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new OutgoingTransactionIntake(store, new FixedTime())
                .AcceptAsync("pacs.008", "CBS-1", "{}", cancellation.Token));
        Assert.Null(store.Received);
    }

    [Theory]
    [InlineData(" ", "{}")]
    [InlineData("CBS-1", " ")]
    public async Task Missing_reference_or_request_does_not_call_storage(string reference, string payload)
    {
        var store = new PendingStore();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new OutgoingTransactionIntake(store, new FixedTime())
                .AcceptAsync("pacs.008", reference, payload, CancellationToken.None));
        Assert.Null(store.Received);
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class PendingStore : ITransactionStore
    {
        public TaskCompletionSource<TransactionIntakeResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PaymentTransaction? Received { get; private set; }
        public string? Payload { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<TransactionIntakeResult> GetOrAddOutgoingAsync(PaymentTransaction transaction, string requestJson, CancellationToken cancellationToken)
        {
            Received = transaction;
            Payload = requestJson;
            CancellationToken = cancellationToken;
            return Completion.Task;
        }

        public Task<PaymentTransaction?> FindAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ReadRequestAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TransactionUpdateResult> TryUpdateAsync(Guid id, Func<PaymentTransaction, bool> change, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
