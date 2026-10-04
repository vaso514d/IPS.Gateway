using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Abstractions.Payments;

public interface IOutgoingPaymentRepository
{
    Task<OutgoingPayment?> FindAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>A detached current-state read, including after a duplicate intake commit.</summary>
    Task<OutgoingPayment?> FindByClientReferenceAsync(string reference, CancellationToken cancellationToken);
    void Add(OutgoingPayment payment, string requestJson);
    Task<string?> ReadRequestAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredPaymentEvent>> ReadEventsAsync(Guid id, CancellationToken cancellationToken);
}
