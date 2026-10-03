using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Transactions;

public sealed class OutgoingTransactionWork(ITransactionWorkStore store, TimeProvider timeProvider)
{
    public Task<TransactionClaim?> TryStartAsync(Guid id, TimeSpan duration, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return store.TryClaimAsync(id, now, duration, transaction =>
        {
            if (transaction.Current.Status != TransactionStatus.Received)
            {
                return false;
            }

            transaction.ChangeStatus(TransactionStatus.Sending, StatusSource.Gateway, now);
            return true;
        }, cancellationToken);
    }

    public Task<TransactionUpdateResult> TryRecoverAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        return store.TryRecoverAsync(id, now, transaction =>
        {
            if (transaction.Current.Status is not
                (TransactionStatus.Sending or TransactionStatus.Investigating or TransactionStatus.Resending))
            {
                return false;
            }

            transaction.ChangeStatus(TransactionStatus.Uncertain, StatusSource.Recovery, now,
                description: $"Recovered: ownership expired while {transaction.Current.Status}; the remote outcome is unknown.");
            return true;
        }, cancellationToken);
    }
}
