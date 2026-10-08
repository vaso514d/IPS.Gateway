using IPS.Middleware.Application.Abstractions.Persistence;

namespace IPS.Middleware.Application.Payments.StatusDelivery;

// Reading a final outcome acknowledges its pending callback: the caller has now seen it.
public sealed class OutgoingStatusReader(IOutgoingStatusRepository repository, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task<OutgoingStatus?> ReadAsync(string messageType, string clientReference, CancellationToken cancellationToken)
    {
        var observed = await repository.ReadAsync(clientReference.Trim(), cancellationToken);
        if (observed is null || observed.MessageType != messageType)
        {
            return null;
        }

        if (observed.IsReportable)
        {
            await TryAcknowledgeAsync(observed, cancellationToken);
        }

        return observed;
    }

    private async Task TryAcknowledgeAsync(OutgoingStatus observed, CancellationToken cancellationToken)
    {
        try
        {
            if (await repository.StageAcknowledgeAsync(observed, timeProvider.GetUtcNow(), cancellationToken))
            {
                await unitOfWork.SaveAsync(cancellationToken);
            }
        }
        catch (PersistenceConcurrencyException)
        {
            // A newer outcome or callback won; the caller still receives the committed snapshot that was read.
        }
    }
}
