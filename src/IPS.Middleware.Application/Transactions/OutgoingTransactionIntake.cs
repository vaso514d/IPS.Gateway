using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Repositories.Payments;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Transactions;

public sealed class OutgoingTransactionIntake(
    IOutgoingPaymentRepository repository, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task<TransactionIntakeResult> AcceptAsync(
        string messageType, string clientReference, string requestJson, CancellationToken cancellationToken)
    {
        var reference = clientReference.Trim();
        var existing = await repository.FindByClientReferenceAsync(reference, cancellationToken);
        if (existing is not null) return new(existing, false);

        var payment = OutgoingPayment.Receive(Guid.NewGuid(), messageType, reference, timeProvider.GetUtcNow());
        repository.Add(payment, requestJson);
        try
        {
            await unitOfWork.SaveAsync(cancellationToken);
            return new(payment, true);
        }
        catch (UniqueConstraintException)
        {
            var winner = await repository.FindByClientReferenceAsync(reference, cancellationToken);
            if (winner is null) throw;
            return new(winner, false);
        }
    }
}
