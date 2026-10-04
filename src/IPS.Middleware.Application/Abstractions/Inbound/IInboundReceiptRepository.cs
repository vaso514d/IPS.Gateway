using IPS.Middleware.Application.Inbound;

namespace IPS.Middleware.Application.Abstractions.Inbound;

public interface IInboundReceiptRepository
{
    Task<InboundRegistration> StageRegistrationAsync(InboundReceipt receipt, CancellationToken cancellationToken);
    Task<StoredInboundReceipt?> ReadAsync(Guid journalId, CancellationToken cancellationToken);
}
