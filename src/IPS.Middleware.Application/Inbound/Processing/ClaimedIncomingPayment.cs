using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Processing;

// An incoming payment owned through a claim. Committed reports only the result this owner actually saved.
public sealed class ClaimedIncomingPayment(IncomingProcessingSnapshot snapshot, IncomingPaymentClaim claim, IUnitOfWork unitOfWork)
{
    public IncomingProcessingSnapshot Snapshot { get; } = snapshot;
    public IncomingPaymentClaim Claim { get; } = claim;
    public IncomingPayment Payment => Snapshot.Payment;
    public IncomingProcessingResult Committed { get; private set; } = IncomingProcessingResult.Of(snapshot.Payment);

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await unitOfWork.SaveAsync(cancellationToken);
        Committed = IncomingProcessingResult.Of(Payment);
    }
}
