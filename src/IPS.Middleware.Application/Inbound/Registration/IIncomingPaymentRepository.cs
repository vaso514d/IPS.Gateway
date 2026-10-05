using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Registration;

public interface IIncomingPaymentRepository
{
    // Exact identity lookup: normalized participant BIC and ordinal EndToEndId. The payment is tracked in this scope.
    Task<RegisteredIncomingPayment?> FindAsync(string participantBic, string endToEndId, CancellationToken cancellationToken);
    // Stage a new payment with its immutable request snapshot.
    void Add(IncomingPayment payment, Pacs008Request request, IncomingProcessingContext context);
}
