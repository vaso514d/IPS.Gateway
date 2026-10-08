using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Abstractions.Payments;

public interface IPaymentPreparationRepository
{
    Task<PreparedPaymentMessage?> ReadAsync(Guid paymentId, CancellationToken cancellationToken);

    // Stage immutable content under a live claim; the shared unit of work makes it durable.
    void StageUnsignedXml(OutgoingPayment payment, TransactionClaim claim, string xml, DateTimeOffset now);
    void StageDevelopmentUnsigned(OutgoingPayment payment, TransactionClaim claim, DateTimeOffset now);
    void StageSignedXml(OutgoingPayment payment, TransactionClaim claim, string xml, DateTimeOffset now);
}
