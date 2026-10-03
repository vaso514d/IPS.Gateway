using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Transactions;

public interface ITransactionStore
{
    /// <summary>Commits the transaction, request, and initial history together, or returns the existing client reference.</summary>
    Task<TransactionIntakeResult> GetOrAddOutgoingAsync(
        PaymentTransaction transaction, string requestJson, CancellationToken cancellationToken);

    Task<PaymentTransaction?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<string?> ReadRequestAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Applies a synchronous decision to a consistent snapshot, then commits its appended history and current status together.
    /// The decision runs once, must not perform external side effects, and may return false to decline the update.
    /// A concurrent writer wins without being overwritten; callers decide whether to retry after reloading.
    /// </summary>
    Task<TransactionUpdateResult> TryUpdateAsync(
        Guid id, Func<PaymentTransaction, bool> change, CancellationToken cancellationToken);
}

public sealed record TransactionIntakeResult(PaymentTransaction Transaction, bool Created);

public enum TransactionUpdateResult
{
    Saved,
    NotFound,
    Unchanged,
    Conflict
}
