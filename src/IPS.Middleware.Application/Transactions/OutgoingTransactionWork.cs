using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Transactions;

public sealed class OutgoingTransactionWork(
    IOutgoingPaymentRepository repository,
    ITransactionWorkRepository work,
    IPaymentSubmissionRepository submissions,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<TransactionClaim?> TryStartAsync(Guid id, TimeSpan duration, CancellationToken cancellationToken)
    {
        var payment = await repository.FindAsync(id, cancellationToken);
        if (payment is not { CurrentStatus: TransactionStatus.Received })
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var claim = work.StageClaim(payment, now, duration);
        if (claim is null)
        {
            return null;
        }

        payment.BeginSending(now);
        return await TrySaveAsync(cancellationToken) ? claim : null;
    }

    public async Task<TransactionWorkResult> TryRecoverAsync(Guid id, CancellationToken cancellationToken)
    {
        var payment = await repository.FindAsync(id, cancellationToken);
        if (payment is null)
        {
            return TransactionWorkResult.NotFound;
        }

        if (payment.CurrentStatus is not (TransactionStatus.Sending or TransactionStatus.Investigating or TransactionStatus.Resending))
        {
            return TransactionWorkResult.Unchanged;
        }

        var resumable = await IsResumableAsync(payment, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (!work.StageRecovery(payment, now))
        {
            return TransactionWorkResult.Unchanged;
        }

        if (!resumable)
        {
            var details = new PaymentDetails(description: $"Recovered: ownership expired while {payment.CurrentStatus}; the remote outcome is unknown.");
            payment.MarkOutcomeUnknown(StatusSource.Recovery, now, details);
        }

        return await TrySaveAsync(cancellationToken) ? TransactionWorkResult.Saved : TransactionWorkResult.Conflict;
    }

    // An abandoned pacs.008 is released for its next owner unless it was submitted without a stored response:
    // preparation is safe to repeat and a stored response is interpreted without sending again.
    // The parent row version fences a checkpoint committed after this read.
    private async Task<bool> IsResumableAsync(OutgoingPayment payment, CancellationToken cancellationToken)
    {
        if (payment.CurrentStatus != TransactionStatus.Sending || payment.MessageType != PaymentMessageTypes.Pacs008)
        {
            return false;
        }

        var submission = await submissions.ReadAsync(payment.Id, cancellationToken);
        return submission is not { Marker: not null, Response: null };
    }

    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveAsync(cancellationToken);
            return true;
        }
        catch (PersistenceConcurrencyException)
        {
            return false;
        }
    }
}
