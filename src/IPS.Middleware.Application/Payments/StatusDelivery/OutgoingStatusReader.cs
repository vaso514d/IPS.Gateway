using IPS.Middleware.Application.Abstractions.Persistence;

namespace IPS.Middleware.Application.Payments.StatusDelivery;

public sealed class OutgoingStatusReader(IOutgoingStatusRepository repository, IUnitOfWork unit, TimeProvider time)
{
    public async Task<OutgoingStatus?> ReadAsync(string messageType, string clientReference, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientReference);
        var reference = clientReference.Trim();
        if (reference.Length > 35)
        {
            throw new ArgumentException("clientReference must be at most 35 characters.", nameof(clientReference));
        }

        var observed = await repository.ReadAsync(reference, cancellationToken);
        if (observed is null || observed.MessageType != messageType)
        {
            return null;
        }

        if (!observed.IsReportable)
        {
            return observed;
        }

        try
        {
            if (await repository.StageAcknowledgeAsync(observed, time.GetUtcNow(), cancellationToken))
            {
                await unit.SaveAsync(cancellationToken);
            }
        }
        // A newer outcome or callback won; return only the committed snapshot we actually read.
        catch (PersistenceConcurrencyException) { }
        return observed;
    }
}
