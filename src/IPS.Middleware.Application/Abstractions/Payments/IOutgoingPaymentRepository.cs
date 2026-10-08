using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Abstractions.Payments;

public interface IOutgoingPaymentRepository
{
    Task<OutgoingPayment?> FindAsync(Guid id, CancellationToken cancellationToken);
    // A detached current-state read, including after a duplicate intake commit.
    Task<OutgoingPayment?> FindByClientReferenceAsync(string reference, CancellationToken cancellationToken);
    // The tracked payment that carries this protocol message id, or null.
    Task<OutgoingPayment?> FindByMessageIdAsync(string messageId, CancellationToken cancellationToken);
    // Whether another payment already carries this protocol message id or transaction id.
    Task<bool> IsProtocolIdUsedAsync(string messageId, string transactionId, CancellationToken cancellationToken);
    // Stage intake; a payment carries the snapshot its processing will use.
    void Add(OutgoingPayment payment, string requestJson, IAcceptedPayment? accepted);
    Task<string?> ReadRequestAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredPaymentEvent>> ReadEventsAsync(Guid id, CancellationToken cancellationToken);
}
