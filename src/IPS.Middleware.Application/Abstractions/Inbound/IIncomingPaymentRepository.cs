using IPS.Middleware.Application.Inbound;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Abstractions.Inbound;

public interface IIncomingPaymentRepository
{
    /// <summary>Exact identity lookup: normalized participant BIC and ordinal EndToEndId. The payment is tracked in this scope.</summary>
    Task<RegisteredIncomingPayment?> FindAsync(string participantBic, string endToEndId, CancellationToken cancellationToken);
    /// <summary>Stage a new payment with its immutable request snapshot.</summary>
    void Add(IncomingPayment payment, Pacs008Request request, IncomingProcessingContext context);
}
