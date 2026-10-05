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

public sealed class PaymentPreparationRepository(TransactionDbContext db) : IPaymentPreparationRepository
{
    public async Task<PreparedPaymentMessage?> ReadAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var stored = await db.OutgoingMetadata.AsNoTracking()
            .Where(p => p.Id == paymentId && p.MessageId != null)
            .Select(p => new
            {
                MessageId = p.MessageId,
                TransactionId = p.ProtocolTransactionId,
                Unsigned = p.UnsignedXml,
                Accepted = p.AcceptedJson
            }).SingleOrDefaultAsync(cancellationToken);
        if (stored is null)
        {
            return null;
        }

        var ready = await db.OutgoingMessages.AsNoTracking()
            .SingleOrDefaultAsync(p => p.PaymentId == paymentId && p.InvestigationId == null && p.Direction == OutgoingMessageDirection.Outbound, cancellationToken);
        return new(stored.MessageId!, stored.TransactionId!, stored.Unsigned,
            ready?.Disposition == SubmissionMessageKind.Signed ? ready.Content : null, PaymentJson.ReadAccepted(stored.Accepted), ready?.Disposition);
    }

    public void StageUnsignedXml(OutgoingPayment payment, TransactionClaim claim, string xml, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        var entry = db.OwnedPacs008(payment, claim, now);
        if (entry.Entity.UnsignedXml is null && OutgoingJournal.Find(db, payment.Id, OutgoingMessageDirection.Outbound) is not null)
        {
            throw new InvalidOperationException("Preparation cannot change after the wire message is frozen.");
        }

        db.WriteUnsignedXml(entry, xml);
    }

    public void StageSignedXml(OutgoingPayment payment, TransactionClaim claim, string xml, DateTimeOffset now) =>
        StageReady(payment, claim, xml, SubmissionMessageKind.Signed, now);

    public void StageDevelopmentUnsigned(OutgoingPayment payment, TransactionClaim claim, DateTimeOffset now)
    {
        var entry = db.OwnedPacs008(payment, claim, now);
        StageReady(payment, claim, entry.Property(p => p.UnsignedXml).OriginalValue
            ?? throw new InvalidOperationException("Commit unsigned XML before selecting it for development transmission."),
            SubmissionMessageKind.DevelopmentUnsigned, now);
    }

    private void StageReady(OutgoingPayment payment, TransactionClaim claim, string xml, SubmissionMessageKind kind, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        var entry = db.OwnedPacs008(payment, claim, now);
        if (entry.Property(p => p.UnsignedXml).OriginalValue is null)
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
        var row = new OutgoingMessageRow
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Direction = OutgoingMessageDirection.Outbound,
            MessageDefinition = "pacs.008.001.12",
            Content = xml,
            CreatedAtUtc = now.ToUniversalTime(),
            Status = MessageJournalStatus.ReadyToSend,
            Disposition = kind
        };
        db.OutgoingMessages.Add(row);
        db.RequireCurrentVersion(payment);
    }
}
