using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Abstractions.Payments;

public interface IOutgoingPaymentRepository
{
    Task<OutgoingPayment?> FindAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>A detached current-state read, including after a duplicate intake commit.</summary>
    Task<OutgoingPayment?> FindByClientReferenceAsync(string reference, CancellationToken cancellationToken);
    /// <summary>Stage intake; a pacs.008 carries the snapshot its processing will use.</summary>
    void Add(OutgoingPayment payment, string requestJson, AcceptedPacs008? accepted);
    Task<string?> ReadRequestAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredPaymentEvent>> ReadEventsAsync(Guid id, CancellationToken cancellationToken);
}
