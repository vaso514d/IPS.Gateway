using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Payments.Investigation;

public interface IResendRepository
{
    Task<ResendAttempt?> ReadAsync(Guid paymentId, CancellationToken cancellationToken);
    // Payments of message types without an investigation that are due their next possible-duplicate attempt.
    Task<IReadOnlyList<Guid>> FindDueAsync(DateTimeOffset now, InvestigationOptions options, CancellationToken cancellationToken);
    void StageAuthorization(OutgoingPayment payment, TransactionClaim claim, Guid investigationId, int number, DateTimeOffset now);
    void StageAttempt(OutgoingPayment payment, TransactionClaim claim, int number, DateTimeOffset deadlineUtc, DateTimeOffset now);
    void StageReady(OutgoingPayment payment, TransactionClaim claim, Guid resendId, DateTimeOffset now);
    void StageSubmission(OutgoingPayment payment, TransactionClaim claim, Guid resendId, DateTimeOffset now);
    void StageResponse(OutgoingPayment payment, TransactionClaim claim, Guid resendId, IpsSubmissionResponse response, DateTimeOffset now);
    void StageResult(
        OutgoingPayment payment,
        TransactionClaim claim,
        Guid resendId,
        IpsReply result,
        string? transportFailure,
        DateTimeOffset now);
}
