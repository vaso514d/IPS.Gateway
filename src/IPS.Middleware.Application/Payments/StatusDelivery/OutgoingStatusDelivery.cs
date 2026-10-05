using IPS.Middleware.Application.Abstractions.Persistence;

namespace IPS.Middleware.Application.Payments.StatusDelivery;

public sealed class OutgoingStatusDelivery(
        IOutgoingStatusRepository repository,
        IOutgoingStatusReceiver receiver,
        IUnitOfWork unit,
        StatusDeliveryOptions options,
        TimeProvider time)
{
    public async Task<StatusDeliveryResult> DeliverAsync(StatusDeliveryKey key, CancellationToken cancellationToken)
    {
        try
        {
            var work = await repository.ReadWorkAsync(key, cancellationToken);
            if (work is null || work.State != StatusDeliveryState.Pending)
            {
                return StatusDeliveryResult.Unavailable;
            }

            var now = time.GetUtcNow();
            if (work.ClaimToken is { } abandoned && work.ClaimExpiresAtUtc <= now)
            {
                return await RecoverAbandonedAsync(key, work, abandoned, now, cancellationToken);
            }
            var claim = await repository.StageClaimAsync(key, now, options.Ownership, cancellationToken);
            if (claim is null)
            {
                return StatusDeliveryResult.Unavailable;
            }

            await unit.SaveAsync(cancellationToken);
            var failure = await SendAsync(work.Status, cancellationToken);
            // The call may have reached CBS; persist evidence even when the execution token was cancelled.
            using var persistence = new CancellationTokenSource(options.PersistenceBudget, time);
            var result = failure is null ? new StatusDeliveryRetry(StatusDeliveryState.Delivered, null)
                : options.AfterFailure(checked(work.Attempts + 1), time.GetUtcNow());
            if (!await repository.StageFinishAsync(key, claim.Value, time.GetUtcNow(), result, failure, false, persistence.Token))
            {
                return StatusDeliveryResult.OwnershipLost;
            }

            await unit.SaveAsync(persistence.Token);
            return Result(result.State);
        }
        catch (PersistenceConcurrencyException)
        {
            return StatusDeliveryResult.OwnershipLost;
        }
    }

    private async Task<StatusDeliveryResult> RecoverAbandonedAsync(
        StatusDeliveryKey key, StatusDeliveryWork work, Guid abandoned, DateTimeOffset now, CancellationToken token)
    {
        var retry = options.AfterFailure(work.Attempts, work.ClaimExpiresAtUtc!.Value);
        if (!await repository.StageFinishAsync(key, abandoned, now, retry,
            "Previous delivery owner expired; CBS receipt is unknown.", true, token))
        {
            return StatusDeliveryResult.OwnershipLost;
        }

        await unit.SaveAsync(token);
        return Result(retry.State);
    }

    private async Task<string?> SendAsync(OutgoingStatus status, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(options.CallTimeout, time);
        using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            var httpStatus = await receiver.SendAsync(status, status.IdempotencyKey, call.Token);
            return httpStatus is < 200 or >= 300 ? $"CBS callback returned HTTP {httpStatus}." : null;
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
