using IPS.Middleware.Application.Abstractions.Payments;
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
        db.RequireUsable();
        return (await db.OutgoingMetadata.Include(p => p.Payment).SingleOrDefaultAsync(p => p.Id == id, cancellationToken))?.Payment;
    }

    public Task<OutgoingPayment?> FindByClientReferenceAsync(string reference, CancellationToken cancellationToken) =>
        db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.ClientReference == reference, cancellationToken);

    public void Add(OutgoingPayment payment, string requestJson, AcceptedPacs008? accepted)
    {
        db.RequireUsable();
        ArgumentException.ThrowIfNullOrWhiteSpace(requestJson);
        if (payment.EventSequence != 1 || payment.PendingEvents.Count != 1 || payment.CurrentStatus != TransactionStatus.Received)
        {
            throw new ArgumentException("Intake requires a new Received payment.", nameof(payment));
        }

        if (accepted is not null && payment.MessageType != Pacs008)
        {
            throw new ArgumentException("Only pacs.008 payments carry an accepted snapshot.", nameof(accepted));
        }

        db.OutgoingMetadata.Add(new OutgoingPaymentMetadata
        {
            Id = payment.Id,
            Payment = payment,
            RequestJson = requestJson,
            AcceptedJson = accepted is null ? null : PaymentJson.WriteAccepted(accepted),
            Direction = TransactionDirection.Outgoing,
            MessageId = payment.MessageType == Pacs008 ? Guid.NewGuid().ToString("N") : null,
            ProtocolTransactionId = payment.MessageType == Pacs008 ? Guid.NewGuid().ToString("N") : null
        });
    }

    public Task<string?> ReadRequestAsync(Guid id, CancellationToken cancellationToken) =>
        db.OutgoingMetadata.AsNoTracking().Where(p => p.Id == id).Select(p => p.RequestJson)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<StoredPaymentEvent>> ReadEventsAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Events.AsNoTracking().Where(e => e.TransactionId == id).OrderBy(e => e.Sequence)
            .Select(e => new StoredPaymentEvent(e.EventId, e.TransactionId, e.Sequence, e.Name, e.SchemaVersion, e.OccurredAtUtc, e.PayloadJson))
            .ToListAsync(cancellationToken);
}
