using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Transactions;

public sealed class OutgoingTransactionIntake(ITransactionStore store, TimeProvider timeProvider)
{
    public Task<TransactionIntakeResult> AcceptAsync(
        string messageType, string clientReference, string requestJson, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestJson);
        cancellationToken.ThrowIfCancellationRequested();

        var transaction = new PaymentTransaction(Guid.NewGuid(), messageType,
            TransactionDirection.Outgoing, timeProvider.GetUtcNow(), clientReference);
        return store.GetOrAddOutgoingAsync(transaction, requestJson, cancellationToken);
    }
}
