using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Payments.Investigation;

public interface IInvestigationRepository
{
    Task<InvestigationAttempt?> ReadAsync(Guid paymentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> FindDueAsync(DateTimeOffset now, InvestigationOptions options, CancellationToken cancellationToken);
    void StageIdentity(OutgoingPayment payment, TransactionClaim claim, InvestigationIdentity identity, DateTimeOffset now);
    void StageUnsigned(OutgoingPayment payment, TransactionClaim claim, Guid attemptId, string xml, DateTimeOffset now);
    void StageReady(OutgoingPayment payment, TransactionClaim claim, Guid attemptId, SignedMessage message, DateTimeOffset now);
    void StageSubmission(OutgoingPayment payment, TransactionClaim claim, Guid attemptId, DateTimeOffset now);
    void StageResponse(OutgoingPayment payment, TransactionClaim claim, Guid attemptId, IpsSubmissionResponse response, DateTimeOffset now);
    void StageResult(OutgoingPayment payment, TransactionClaim claim, Guid attemptId, InvestigationReply result, string? transportFailure, DateTimeOffset now);
}
