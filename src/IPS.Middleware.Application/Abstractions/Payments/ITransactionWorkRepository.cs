using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Abstractions.Payments;

public interface ITransactionWorkRepository
{
    Task<IReadOnlyList<Guid>> FindDueAsync(TransactionStatus status, DateTimeOffset now, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> FindExpiredAsync(DateTimeOffset now, int take, CancellationToken cancellationToken);

    // Stage metadata on an aggregate tracked by this unit; ownership is acquired only after the save commits.
    TransactionClaim? StageClaim(OutgoingPayment payment, DateTimeOffset now, TimeSpan duration);
    bool StageCompletion(OutgoingPayment payment, TransactionClaim claim, DateTimeOffset now, DateTimeOffset? nextActionAtUtc);
    bool StageRecovery(OutgoingPayment payment, DateTimeOffset now);

    // Prepare the payment for an IPS report: drop an expired claim and automatic scheduling, and fence the row version.
    // False while another owner holds a live claim.
    bool StageSettlement(OutgoingPayment payment, DateTimeOffset now);

    // Fence the row version for a change that does not touch ownership or scheduling.
    void StageObservation(OutgoingPayment payment);
}
