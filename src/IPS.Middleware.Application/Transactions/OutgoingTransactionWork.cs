using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Transactions;

public sealed class OutgoingTransactionWork(
    IOutgoingPaymentRepository repository, ITransactionWorkRepository work, IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<TransactionClaim?> TryStartAsync(Guid id, TimeSpan duration, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw new ArgumentException("A payment identity is required.", nameof(id));
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        var payment = await repository.FindAsync(id, cancellationToken);
        if (payment is null || payment.CurrentStatus != TransactionStatus.Received) return null;
        var now = timeProvider.GetUtcNow();
        var claim = work.StageClaim(payment, now, duration);
        if (claim is null) return null;
        payment.BeginSending(now);
        try { await unitOfWork.SaveAsync(cancellationToken); return claim; }
        catch (PersistenceConcurrencyException) { return null; }
    }

    public async Task<TransactionWorkResult> TryRecoverAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty) throw new ArgumentException("A payment identity is required.", nameof(id));
        var payment = await repository.FindAsync(id, cancellationToken);
        if (payment is null) return TransactionWorkResult.NotFound;
        if (payment.CurrentStatus is not (TransactionStatus.Sending or TransactionStatus.Investigating or TransactionStatus.Resending))
            return TransactionWorkResult.Unchanged;
        var now = timeProvider.GetUtcNow();
        if (!work.StageRecovery(payment, now)) return TransactionWorkResult.Unchanged;
        payment.MarkOutcomeUnknown(StatusSource.Recovery, now,
            new(description: $"Recovered: ownership expired while {payment.CurrentStatus}; the remote outcome is unknown."));
        try { await unitOfWork.SaveAsync(cancellationToken); return TransactionWorkResult.Saved; }
        catch (PersistenceConcurrencyException) { return TransactionWorkResult.Conflict; }
    }
}
