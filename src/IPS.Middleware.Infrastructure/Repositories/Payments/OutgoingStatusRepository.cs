using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Repositories.Payments;

// Only the delivery for the payment's current outcome is live; older outcomes are superseded.
public sealed class OutgoingStatusRepository(TransactionDbContext db) : IOutgoingStatusRepository
{
    private const int MaxFailureLength = 2000;

    public async Task<OutgoingStatus?> ReadAsync(string reference, CancellationToken cancellationToken)
    {
        var metadata = await db.OutgoingMetadata
            .AsNoTracking()
            .Include(x => x.Payment)
            .SingleOrDefaultAsync(x => x.Payment.ClientReference == reference, cancellationToken);
        return metadata is null ? null : OutgoingStatusProjection.Read(metadata);
    }

    public async Task<IReadOnlyList<StatusDeliveryKey>> FindDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken) =>
        await Due(now)
            .OrderBy(x => x.NextAtUtc)
            .ThenBy(x => x.PaymentId)
            .ThenBy(x => x.Sequence)
            .Select(x => new StatusDeliveryKey(x.PaymentId, x.Sequence))
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<StatusDeliveryKey?> FindDueAsync(Guid paymentId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await Due(now)
            .Where(x => x.PaymentId == paymentId)
            .Select(x => new StatusDeliveryKey(x.PaymentId, x.Sequence))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<StatusDeliveryWork?> ReadWorkAsync(StatusDeliveryKey key, CancellationToken cancellationToken)
    {
        var delivery = await LoadAsync(key, cancellationToken);
        if (delivery is null)
        {
            return null;
        }

        return new StatusDeliveryWork(
            key,
            delivery.Payload(),
            delivery.State,
            delivery.Attempts,
            delivery.NextAtUtc,
            delivery.ClaimToken,
            delivery.ClaimExpiresAtUtc);
    }

    public async Task<Guid?> StageClaimAsync(StatusDeliveryKey key, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken)
    {
        var delivery = await LoadAsync(key, cancellationToken);
        if (delivery is not { State: StatusDeliveryState.Pending, ClaimToken: null } || delivery.NextAtUtc > now)
        {
            return null;
        }

        delivery.ClaimToken = Guid.NewGuid();
        delivery.ClaimExpiresAtUtc = now.ToUniversalTime() + duration;
        delivery.Attempts = checked(delivery.Attempts + 1);
        RequireCurrentPaymentVersion(delivery);
        return delivery.ClaimToken;
    }

    public async Task<bool> StageFinishAsync(
        StatusDeliveryKey key,
        Guid token,
        DateTimeOffset now,
        StatusDeliveryRetry result,
        string? failure,
        bool expired,
        CancellationToken cancellationToken)
    {
        var delivery = await LoadAsync(key, cancellationToken);
        if (delivery is not { State: StatusDeliveryState.Pending } || delivery.ClaimToken != token)
        {
            return false;
        }

        // Recovery finishes only expired claims; the owner finishes only its live claim.
        var claimExpired = delivery.ClaimExpiresAtUtc <= now;
        if (claimExpired != expired)
        {
            return false;
        }

        if (db.Entry(delivery).Property(x => x.ClaimToken).OriginalValue != token)
        {
            throw new InvalidOperationException("Commit ownership before completing delivery.");
        }

        delivery.State = result.State;
        delivery.NextAtUtc = result.NextAtUtc;
        delivery.DeliveredAtUtc = result.State == StatusDeliveryState.Delivered ? now.ToUniversalTime() : null;
        delivery.ClaimToken = null;
        delivery.ClaimExpiresAtUtc = null;
        // A success keeps the last failure: it says why an earlier attempt may have reached the core without being recorded.
        if (failure is not null)
        {
            delivery.LastFailure = failure.Length > MaxFailureLength ? failure[..MaxFailureLength] : failure;
        }

        RequireCurrentPaymentVersion(delivery);
        return true;
    }

    public async Task<bool> StageAcknowledgeAsync(OutgoingStatus observed, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var delivery = await LoadAsync(new StatusDeliveryKey(observed.PaymentId, observed.Sequence), cancellationToken);
        if (delivery is not { State: StatusDeliveryState.Pending })
        {
            return false;
        }

        delivery.State = StatusDeliveryState.Delivered;
        delivery.DeliveredAtUtc = now.ToUniversalTime();
        delivery.NextAtUtc = null;
        delivery.ClaimToken = null;
        delivery.ClaimExpiresAtUtc = null;
        RequireCurrentPaymentVersion(delivery);
        return true;
    }

    private IQueryable<OutgoingStatusDeliveryRow> Due(DateTimeOffset now) => CurrentDeliveries()
        .AsNoTracking()
        .Where(x => x.State == StatusDeliveryState.Pending)
        .Where(x => x.ClaimToken == null ? x.NextAtUtc <= now : x.ClaimExpiresAtUtc <= now);

    private IQueryable<OutgoingStatusDeliveryRow> CurrentDeliveries() =>
        db.OutgoingStatusDeliveries.Where(x => db.Payments.Any(p => p.Id == x.PaymentId && p.CurrentSequence == x.Sequence));

    private async Task<OutgoingStatusDeliveryRow?> LoadAsync(StatusDeliveryKey key, CancellationToken cancellationToken)
    {
        var delivery = await CurrentDeliveries()
            .SingleOrDefaultAsync(x => x.PaymentId == key.PaymentId && x.Sequence == key.Sequence, cancellationToken);
        if (delivery is null)
        {
            return null;
        }

        // The parent may have changed between queries; fence the same outcome that was selected.
        var metadata = await db.OutgoingMetadata
            .Include(x => x.Payment)
            .SingleAsync(x => x.Id == key.PaymentId, cancellationToken);
        return metadata.Payment.CurrentSequence == key.Sequence ? delivery : null;
    }

    private void RequireCurrentPaymentVersion(OutgoingStatusDeliveryRow delivery)
    {
        var payment = db.Payments.Local.Single(x => x.Id == delivery.PaymentId);
        db.RequireCurrentVersion(payment);
    }
}
