using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Repositories.Payments;

public sealed class OutgoingPaymentRepository(TransactionDbContext db) : IOutgoingPaymentRepository
{
    public async Task<OutgoingPayment?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        return await db.Payments.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public Task<OutgoingPayment?> FindByClientReferenceAsync(string reference, CancellationToken cancellationToken) =>
        db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.ClientReference == reference, cancellationToken);

    public void Add(OutgoingPayment payment, string requestJson)
    {
        db.RequireUsable();
        ArgumentException.ThrowIfNullOrWhiteSpace(requestJson);
        if (payment.EventSequence != 1 || payment.PendingEvents.Count != 1 || payment.CurrentStatus != TransactionStatus.Received)
            throw new ArgumentException("Intake requires a new Received payment.", nameof(payment));
        var entry = db.Payments.Add(payment);
        entry.Property<string>(RequestJson).CurrentValue = requestJson;
        entry.Property<TransactionDirection>(Direction).CurrentValue = TransactionDirection.Outgoing;
        if (payment.MessageType == Pacs008)
        {
            entry.TextOf(MessageId).CurrentValue = Guid.NewGuid().ToString("N");
            entry.TextOf(ProtocolTransactionId).CurrentValue = Guid.NewGuid().ToString("N");
        }
    }

    public Task<string?> ReadRequestAsync(Guid id, CancellationToken cancellationToken) =>
        db.Payments.AsNoTracking().Where(p => p.Id == id).Select(p => EF.Property<string>(p, RequestJson))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<StoredPaymentEvent>> ReadEventsAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Events.AsNoTracking().Where(e => e.TransactionId == id).OrderBy(e => e.Sequence)
            .Select(e => new StoredPaymentEvent(e.EventId, e.TransactionId, e.Sequence, e.Name, e.SchemaVersion, e.OccurredAtUtc, e.PayloadJson))
            .ToListAsync(cancellationToken);
}
