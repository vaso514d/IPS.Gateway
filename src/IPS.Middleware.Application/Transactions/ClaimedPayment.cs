using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Transactions;

// A payment owned through a claim. Committed reports only the outcome this owner actually saved.
public sealed class ClaimedPayment
{
    private readonly ITransactionWorkRepository _work;
    private readonly IUnitOfWork _unitOfWork;

    private ClaimedPayment(OutgoingPayment payment, TransactionClaim claim, ITransactionWorkRepository work, IUnitOfWork unitOfWork)
    {
        Payment = payment;
        Claim = claim;
        Committed = payment.Current;
        _work = work;
        _unitOfWork = unitOfWork;
    }

    public OutgoingPayment Payment { get; }
    public TransactionClaim Claim { get; }
    public PaymentOutcome Committed { get; private set; }

    // Stages the claim; the caller commits it together with its first transition.
    public static ClaimedPayment? TryStage(
        OutgoingPayment payment,
        ITransactionWorkRepository work,
        IUnitOfWork unitOfWork,
        DateTimeOffset now,
        TimeSpan ownership)
    {
        var claim = work.StageClaim(payment, now, ownership);
        return claim is null ? null : new ClaimedPayment(payment, claim, work, unitOfWork);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        await _unitOfWork.SaveAsync(cancellationToken);
        Committed = Payment.Current;
    }

    // Whatever the owner recorded commits together with the ownership release.
    public async Task ReleaseAsync(DateTimeOffset now, DateTimeOffset? nextActionAtUtc, CancellationToken cancellationToken)
    {
        if (!_work.StageCompletion(Payment, Claim, now, nextActionAtUtc))
        {
            throw new PersistenceConcurrencyException("Ownership expired before the result could be stored.");
        }

        await CommitAsync(cancellationToken);
    }
}
