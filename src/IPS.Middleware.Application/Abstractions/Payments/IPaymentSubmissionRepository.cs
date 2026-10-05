using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Abstractions.Payments;

public interface IPaymentSubmissionRepository
{
    Task<IReadOnlyList<OutgoingMessage>> ReadJournalAsync(Guid paymentId, CancellationToken cancellationToken);
    void StageInterpretation(OutgoingPayment payment, TransactionClaim claim, IpsReply reply, DateTimeOffset now);

    Task<PaymentSubmission?> ReadAsync(Guid paymentId, CancellationToken cancellationToken);

    // Stage initial submission once. The caller must commit successfully before remote I/O.
    void StageSubmission(OutgoingPayment payment, TransactionClaim claim, SubmissionMessageKind messageKind, DateTimeOffset now);
    void StageResponse(OutgoingPayment payment, TransactionClaim claim, IpsSubmissionResponse response, DateTimeOffset now);
}
