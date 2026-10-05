using IPS.Middleware.Application.Payments.StatusDelivery;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Repositories.Payments;

public sealed class OutgoingStatusRepository(TransactionDbContext db) : IOutgoingStatusRepository
{
    public async Task<OutgoingStatus?> ReadAsync(string reference, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        var metadata = await db.OutgoingMetadata.AsNoTracking().Include(p => p.Payment)
            .SingleOrDefaultAsync(p => p.Payment.ClientReference == reference, cancellationToken);
        if (metadata is null)
        {
            return null;
        }
        var payment = metadata.Payment;
        return new(payment.Id, payment.CurrentSequence, payment.MessageType, payment.ClientReference,
            payment.CurrentStatus, payment.CurrentStatusAtUtc, payment.Current.Details, metadata.MessageId,
            PaymentJson.ReadAccepted(metadata.AcceptedJson)?.Payment.EndToEndId);
    }

    public async Task<IReadOnlyList<StatusDeliveryKey>> FindDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        if (take is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(take));
        }

        return await Current().AsNoTracking().Where(r => r.State == StatusDeliveryState.Pending &&
                (r.ClaimToken == null ? r.NextAtUtc <= now : r.ClaimExpiresAtUtc <= now))
            .OrderBy(r => r.NextAtUtc).ThenBy(r => r.PaymentId).ThenBy(r => r.Sequence)
            .Select(r => new StatusDeliveryKey(r.PaymentId, r.Sequence)).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<StatusDeliveryWork?> ReadWorkAsync(StatusDeliveryKey key, CancellationToken cancellationToken)
    {
        var row = await LoadAsync(key, cancellationToken);
        return row is null ? null : new(key, row.Payload(), row.State, row.Attempts, row.NextAtUtc, row.ClaimToken, row.ClaimExpiresAtUtc);
    }

    public async Task<Guid?> StageClaimAsync(StatusDeliveryKey key, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var row = await LoadAsync(key, cancellationToken);
        if (row is null || row.State != StatusDeliveryState.Pending || row.ClaimToken is not null || row.NextAtUtc > now)
        {
            return null;
        }

        row.ClaimToken = Guid.NewGuid();
        row.ClaimExpiresAtUtc = now.ToUniversalTime() + duration;
        row.Attempts = checked(row.Attempts + 1);
        Authorize(row);
        return row.ClaimToken;
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
        var row = await LoadAsync(key, cancellationToken);
        if (row is null || row.State != StatusDeliveryState.Pending || row.ClaimToken != token || token == Guid.Empty ||
            (expired ? row.ClaimExpiresAtUtc > now : row.ClaimExpiresAtUtc <= now))
        {
            return false;
        }

        if (db.Entry(row).Property(p => p.ClaimToken).OriginalValue != token)
        {
            throw new InvalidOperationException("Commit ownership before completing delivery.");
        }

        row.State = result.State;
        row.NextAtUtc = result.NextAtUtc;
        row.DeliveredAtUtc = result.State == StatusDeliveryState.Delivered ? now.ToUniversalTime() : null;
        row.ClaimToken = null;
        row.ClaimExpiresAtUtc = null;
        row.LastFailure = failure?.Length > 2000 ? failure[..2000] : failure;
        Authorize(row);
        return true;
    }

    public async Task<bool> StageAcknowledgeAsync(OutgoingStatus observed, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var row = await LoadAsync(new(observed.PaymentId, observed.Sequence), cancellationToken);
        if (row is null || row.State != StatusDeliveryState.Pending)
        {
            return false;
        }

        row.State = StatusDeliveryState.Delivered;
        row.DeliveredAtUtc = now.ToUniversalTime();
        row.NextAtUtc = null;
        row.ClaimToken = null;
        row.ClaimExpiresAtUtc = null;
        Authorize(row);
        return true;
    }

    private IQueryable<OutgoingStatusDeliveryRow> Current() => db.OutgoingStatusDeliveries
        .Where(r => db.Payments.Any(p => p.Id == r.PaymentId && p.CurrentSequence == r.Sequence));

    private async Task<OutgoingStatusDeliveryRow?> LoadAsync(StatusDeliveryKey key, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        var row = await Current().SingleOrDefaultAsync(r => r.PaymentId == key.PaymentId && r.Sequence == key.Sequence, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var metadata = await db.OutgoingMetadata.Include(p => p.Payment).SingleAsync(p => p.Id == key.PaymentId, cancellationToken);
        var payment = metadata.Payment;
        // The parent may have changed between queries; fence the same outcome we selected.
        return payment.CurrentSequence == key.Sequence ? row : null;
    }

    private void Authorize(OutgoingStatusDeliveryRow row)
    {
        var payment = db.Payments.Local.Single(p => p.Id == row.PaymentId);
        db.Entry(db.Metadata(payment)).Property(p => p.NextActionAtUtc).IsModified = true;
        db.Changes.AuthorizedOwnership.Add(payment.Id);
        db.Changes.StatusChanges.Authorize(db.Entry(row));
    }
}
