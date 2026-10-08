using IPS.Middleware.Application.Abstractions.Persistence;

namespace IPS.Middleware.Application.Inbound.Receipts;

public sealed class InboundReceiptIntake(IInboundReceiptRepository repository, IUnitOfWork unitOfWork)
{
    public async Task<InboundRegistration> RegisterAsync(InboundReceipt receipt, CancellationToken cancellationToken)
    {
        var result = await repository.StageRegistrationAsync(receipt, cancellationToken);
        await unitOfWork.SaveAsync(cancellationToken);
        return result;
    }
}
