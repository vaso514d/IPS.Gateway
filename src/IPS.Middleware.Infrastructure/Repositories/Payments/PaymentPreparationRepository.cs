using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Repositories.Payments;

// Each artifact is written once; repeating identical content is a no-op, different content is refused.
public sealed class PaymentPreparationRepository(TransactionDbContext db) : IPaymentPreparationRepository
{
    public async Task<PreparedPaymentMessage?> ReadAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var stored = await db.OutgoingMetadata
            .AsNoTracking()
            .Where(x => x.Id == paymentId && x.MessageId != null)
            .Select(x => new { x.MessageId, x.ProtocolTransactionId, x.UnsignedXml, x.AcceptedJson })
            .SingleOrDefaultAsync(cancellationToken);
        if (stored is null)
        {
            return null;
        }

        var ready = await db.OutgoingMessages
            .AsNoTracking()
            .Where(x => x.PaymentId == paymentId && x.Direction == OutgoingMessageDirection.Outbound)
            .Where(OutgoingJournal.IsInitial)
            .SingleOrDefaultAsync(cancellationToken);
        var signedXml = ready?.Disposition == SubmissionMessageKind.Signed ? ready.Content : null;

        return new PreparedPaymentMessage(
            stored.MessageId!,
            stored.ProtocolTransactionId!,
            stored.UnsignedXml,
            signedXml,
            PaymentJson.ReadAccepted(stored.AcceptedJson),
            ready?.Disposition);
    }

    public void StageUnsignedXml(OutgoingPayment payment, TransactionClaim claim, string xml, DateTimeOffset now)
    {
        var metadata = db.OwnedPacs008(payment, claim, now, TransactionStatus.Sending).Entity;
        if (metadata.UnsignedXml is { } existing)
        {
            if (existing != xml)
            {
                throw new InvalidOperationException("Stored payment artifacts cannot be replaced.");
            }

            return;
        }

        if (OutgoingJournal.Find(db, payment.Id, OutgoingMessageDirection.Outbound) is not null)
        {
            throw new InvalidOperationException("Preparation cannot change after the wire message is frozen.");
        }

        metadata.UnsignedXml = xml;
    }

    public void StageSignedXml(OutgoingPayment payment, TransactionClaim claim, string xml, DateTimeOffset now) =>
        StageReady(payment, claim, xml, SubmissionMessageKind.Signed, now);

    public void StageDevelopmentUnsigned(OutgoingPayment payment, TransactionClaim claim, DateTimeOffset now)
    {
        var unsignedXml = CommittedUnsignedXml(payment, claim, now)
            ?? throw new InvalidOperationException("Commit unsigned XML before selecting it for development transmission.");
        StageReady(payment, claim, unsignedXml, SubmissionMessageKind.DevelopmentUnsigned, now);
    }

    private void StageReady(OutgoingPayment payment, TransactionClaim claim, string xml, SubmissionMessageKind kind, DateTimeOffset now)
    {
        if (CommittedUnsignedXml(payment, claim, now) is null)
        {
            throw new InvalidOperationException("Commit unsigned XML before freezing the wire message.");
        }

        if (OutgoingJournal.Find(db, payment.Id, OutgoingMessageDirection.Outbound) is { } existing)
        {
            if (existing.Content != xml || existing.Disposition != kind)
            {
                throw new InvalidOperationException("The frozen wire message cannot be replaced or downgraded.");
            }

            return;
        }

        db.OutgoingMessages.Add(new OutgoingMessageRow
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Direction = OutgoingMessageDirection.Outbound,
            MessageDefinition = PaymentMessageTypes.Pacs008Definition,
            Content = xml,
            CreatedAtUtc = now.ToUniversalTime(),
            Status = MessageJournalStatus.ReadyToSend,
            Disposition = kind
        });
        db.RequireCurrentVersion(payment);
    }

    private string? CommittedUnsignedXml(OutgoingPayment payment, TransactionClaim claim, DateTimeOffset now) =>
        db.OwnedPacs008(payment, claim, now, TransactionStatus.Sending).Property(x => x.UnsignedXml).OriginalValue;
}
