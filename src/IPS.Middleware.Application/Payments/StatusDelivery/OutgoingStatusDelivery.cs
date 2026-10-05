using IPS.Middleware.Application.Abstractions.Persistence;

namespace IPS.Middleware.Application.Payments.StatusDelivery;

// Delivers one outcome callback to CBS; the call is claimed before it is made and its result is stored afterwards.
public sealed class OutgoingStatusDelivery(
    IOutgoingStatusRepository repository,
    IOutgoingStatusReceiver receiver,
    IUnitOfWork unitOfWork,
    StatusDeliveryOptions options,
    TimeProvider timeProvider)
{
    private const string AbandonedDelivery = "Previous delivery owner expired; CBS receipt is unknown.";

    public async Task<StatusDeliveryResult> DeliverAsync(StatusDeliveryKey key, CancellationToken cancellationToken)
    {
        try
        {
            var work = await repository.ReadWorkAsync(key, cancellationToken);
            if (work is not { State: StatusDeliveryState.Pending })
            {
                return StatusDeliveryResult.Unavailable;
            }

            var now = timeProvider.GetUtcNow();
            if (work.ClaimToken is { } abandonedClaim && work.ClaimExpiresAtUtc <= now)
            {
                return await RecoverAbandonedAsync(key, work, abandonedClaim, now, cancellationToken);
            }

            var claim = await repository.StageClaimAsync(key, now, options.Ownership, cancellationToken);
            if (claim is null)
            {
                return StatusDeliveryResult.Unavailable;
            }

            await unitOfWork.SaveAsync(cancellationToken);
            return await DeliverClaimedAsync(key, work, claim.Value, cancellationToken);
        }
        catch (PersistenceConcurrencyException)
        {
            return StatusDeliveryResult.OwnershipLost;
        }
    }

    private async Task<StatusDeliveryResult> DeliverClaimedAsync(StatusDeliveryKey key, StatusDeliveryWork work, Guid claim, CancellationToken cancellationToken)
    {
        var failure = await SendAsync(work.Status, cancellationToken);

        // The call may have reached CBS; persist evidence even when the execution token was cancelled.
        using var persistence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
        var retry = failure is null
            ? new StatusDeliveryRetry(StatusDeliveryState.Delivered, null)
            : options.AfterFailure(checked(work.Attempts + 1), timeProvider.GetUtcNow());
        var finished = await repository.StageFinishAsync(key, claim, timeProvider.GetUtcNow(), retry, failure, false, persistence.Token);
        if (!finished)
        {
            return StatusDeliveryResult.OwnershipLost;
        }

        await unitOfWork.SaveAsync(persistence.Token);
        return Result(retry.State);
    }

    private async Task<StatusDeliveryResult> RecoverAbandonedAsync(
        StatusDeliveryKey key,
        StatusDeliveryWork work,
        Guid abandonedClaim,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var retry = options.AfterFailure(work.Attempts, work.ClaimExpiresAtUtc!.Value);
        var finished = await repository.StageFinishAsync(key, abandonedClaim, now, retry, AbandonedDelivery, true, cancellationToken);
        if (!finished)
        {
            return StatusDeliveryResult.OwnershipLost;
        }

        await unitOfWork.SaveAsync(cancellationToken);
        return Result(retry.State);
    }

    private async Task<string?> SendAsync(OutgoingStatus status, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(options.CallTimeout, timeProvider);
        using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            var httpStatus = await receiver.SendAsync(status, status.IdempotencyKey, call.Token);
            return httpStatus is >= 200 and < 300 ? null : $"CBS callback returned HTTP {httpStatus}.";
        }
        catch (Exception exception)
        {
            return $"{exception.GetType().Name}: {exception.Message}";
        }
    }

    private static StatusDeliveryResult Result(StatusDeliveryState state) => state switch
    {
        StatusDeliveryState.Delivered => StatusDeliveryResult.Delivered,
        StatusDeliveryState.Exhausted => StatusDeliveryResult.Exhausted,
        _ => StatusDeliveryResult.Scheduled
    };
}
