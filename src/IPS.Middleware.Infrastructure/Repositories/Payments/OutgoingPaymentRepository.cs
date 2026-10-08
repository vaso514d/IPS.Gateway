using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Repositories.Payments;

public sealed class OutgoingPaymentRepository(TransactionDbContext db) : IOutgoingPaymentRepository
{
    public async Task<OutgoingPayment?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var metadata = await db.OutgoingMetadata
            .Include(x => x.Payment)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return metadata?.Payment;
    }

    public async Task<OutgoingPayment?> FindByMessageIdAsync(string messageId, CancellationToken cancellationToken)
    {
        var metadata = await db.OutgoingMetadata
            .Include(x => x.Payment)
            .SingleOrDefaultAsync(x => x.MessageId == messageId, cancellationToken);
        return metadata?.Payment;
    }

    public Task<bool> IsProtocolIdUsedAsync(string messageId, string transactionId, CancellationToken cancellationToken) =>
        db.OutgoingMetadata.AnyAsync(x => x.MessageId == messageId || x.ProtocolTransactionId == transactionId, cancellationToken);

    public Task<OutgoingPayment?> FindByClientReferenceAsync(string reference, CancellationToken cancellationToken) =>
        db.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ClientReference == reference, cancellationToken);

    public void Add(OutgoingPayment payment, string requestJson, IAcceptedPayment? accepted)
    {
        var ids = accepted?.SuppliedIds ?? (PaymentMessageTypes.IsOutgoing(payment.MessageType) ? new ProtocolIds(NewProtocolId(), NewProtocolId()) : null);
        db.OutgoingMetadata.Add(new OutgoingPaymentMetadata
        {
            Id = payment.Id,
            Payment = payment,
            RequestJson = requestJson,
            AcceptedJson = accepted is null ? null : PaymentJson.WriteAccepted(payment.MessageType, accepted),
            Direction = TransactionDirection.Outgoing,
            MessageId = ids?.MessageId,
            ProtocolTransactionId = ids?.TransactionId
        });
    }

    public Task<string?> ReadRequestAsync(Guid id, CancellationToken cancellationToken) =>
        db.OutgoingMetadata
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => x.RequestJson)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<StoredPaymentEvent>> ReadEventsAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Events
            .AsNoTracking()
            .Where(x => x.TransactionId == id)
            .OrderBy(x => x.Sequence)
            .Select(x => new StoredPaymentEvent(x.EventId, x.TransactionId, x.Sequence, x.Name, x.SchemaVersion, x.OccurredAtUtc, x.PayloadJson))
            .ToListAsync(cancellationToken);

    private static string NewProtocolId() => Guid.NewGuid().ToString("N");
}
