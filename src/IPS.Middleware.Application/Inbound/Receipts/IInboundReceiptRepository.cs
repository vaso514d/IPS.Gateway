using IPS.Middleware.Application.Inbound.Pacs008;

namespace IPS.Middleware.Application.Inbound.Receipts;

public interface IInboundReceiptRepository
{
    Task<InboundRegistration> StageRegistrationAsync(InboundReceipt receipt, CancellationToken cancellationToken);
    Task<StoredInboundReceipt?> ReadAsync(Guid journalId, CancellationToken cancellationToken);
    // Read saved references even when the receipt is held without a payment attachment.
    Task<IncomingPacs008Reference?> ReadOriginalReferencesAsync(Guid journalId, CancellationToken cancellationToken);
}
