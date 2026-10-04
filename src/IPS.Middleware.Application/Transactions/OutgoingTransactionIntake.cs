using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Transactions;

public sealed class OutgoingTransactionIntake(
    IOutgoingPaymentRepository repository, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task<TransactionIntakeResult> AcceptAsync(
        ValidatedIntakeRequest request, CancellationToken cancellationToken)
    {
        var reference = request.ClientReference;
        var existing = await repository.FindByClientReferenceAsync(reference, cancellationToken);
        if (existing is not null) return new(existing, false);

        var payment = OutgoingPayment.Receive(Guid.NewGuid(), request.MessageType, reference, timeProvider.GetUtcNow());
        repository.Add(payment, request.RequestJson);
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
