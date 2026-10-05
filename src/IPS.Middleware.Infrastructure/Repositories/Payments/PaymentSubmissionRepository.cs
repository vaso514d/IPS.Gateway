using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Repositories.Payments;

public sealed class PaymentSubmissionRepository(TransactionDbContext db) : IPaymentSubmissionRepository
{
    public async Task<IReadOnlyList<OutgoingMessage>> ReadJournalAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        var rows = await db.OutgoingMessages.AsNoTracking().Where(p => p.PaymentId == paymentId)
            .OrderBy(p => p.Direction).ToListAsync(cancellationToken);
        return Array.AsReadOnly(rows.Select(p => p.Snapshot()).ToArray());
    }

    public async Task<PaymentSubmission?> ReadAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        db.RequireUsable();
        if (!await db.Payments.AnyAsync(p => p.Id == paymentId && p.MessageType == Pacs008, cancellationToken))
        {
            return null;
        }

        var rows = await ReadJournalAsync(paymentId, cancellationToken);
        return new(rows.SingleOrDefault(p => p.InvestigationId == null && p.Direction == OutgoingMessageDirection.Outbound)?.Submission,
            rows.SingleOrDefault(p => p.InvestigationId == null && p.Direction == OutgoingMessageDirection.Response)?.Response);
    }

    public void StageSubmission(OutgoingPayment payment, TransactionClaim claim, SubmissionMessageKind messageKind, DateTimeOffset now)
    {
        if (!Enum.IsDefined(messageKind))
        {
            throw new ArgumentOutOfRangeException(nameof(messageKind));
        }

        db.OwnedPacs008(payment, claim, now);
        var row = Committed(payment.Id, OutgoingMessageDirection.Outbound);
        if (row.Status != MessageJournalStatus.ReadyToSend || row.Disposition != messageKind)
        {
            throw new InvalidOperationException("The committed message must be ready and must match the selected disposition.");
        }

        row.Status = MessageJournalStatus.SendStarted;
        row.StartedAtUtc = now.ToUniversalTime();
        row.SubmissionOwner = claim.Token;
        OutgoingJournal.Authorize(db, payment, row);
    }

    public void StageResponse(OutgoingPayment payment, TransactionClaim claim, IpsSubmissionResponse response, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(response);
        db.OwnedPacs008(payment, claim, now);
        var sent = Committed(payment.Id, OutgoingMessageDirection.Outbound);
        if (db.Entry(sent).Property(p => p.Status).OriginalValue != MessageJournalStatus.SendStarted)
        {
            throw new InvalidOperationException("Commit submission before recording its response.");
        }

        if (sent.SubmissionOwner != claim.Token)
        {
            throw new PersistenceConcurrencyException("The response belongs to a different submission owner.");
        }

        var headers = PaymentJson.Write(response.Headers);
        if (OutgoingJournal.Find(db, payment.Id, OutgoingMessageDirection.Response) is { } existing)
        {
            if (existing.Content != response.Body || existing.HttpStatusCode != response.HttpStatusCode || existing.HeadersJson != headers)
            {
                throw new InvalidOperationException("Response evidence cannot be replaced.");
            }

            return;
        }
        var row = new OutgoingMessageRow
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Direction = OutgoingMessageDirection.Response,
            Content = response.Body,
            CreatedAtUtc = now.ToUniversalTime(),
            OriginatingMessageId = sent.Id,
            Status = MessageJournalStatus.Received,
            HttpStatusCode = response.HttpStatusCode,
            HeadersJson = headers
        };
        db.OutgoingMessages.Add(row);
        OutgoingJournal.Authorize(db, payment, row);
    }

    public void StageInterpretation(OutgoingPayment payment, TransactionClaim claim, IpsReply reply, DateTimeOffset now)
    {
        db.OwnedPacs008(payment, claim, now);
        var row = Committed(payment.Id, OutgoingMessageDirection.Response);
        if (row.Status != MessageJournalStatus.Received)
        {
            throw new InvalidOperationException("Only unconsumed response evidence can be interpreted.");
        }

        var conclusive = reply.Status is IpsReplyStatus.Accepted or IpsReplyStatus.Rejected;
        row.Status = conclusive ? MessageJournalStatus.Processed : MessageJournalStatus.Failed;
        // Only a validated, correlated response establishes its protocol definition.
        row.MessageDefinition = conclusive ? "pacs.002.001.14" : null;
        row.ProcessedAtUtc = now.ToUniversalTime();
        row.Failure = conclusive ? null : reply.Details.Description ?? "The response did not establish a valid correlated final outcome.";
        if (row.Failure?.Length > 2000)
        {
            row.Failure = row.Failure[..2000];
        }

        OutgoingJournal.Authorize(db, payment, row);
    }

    private OutgoingMessageRow Committed(Guid paymentId, OutgoingMessageDirection direction)
    {
        var row = OutgoingJournal.Find(db, paymentId, direction);
        if (row is null || db.Entry(row).State == EntityState.Added)
        {
            throw new InvalidOperationException("Commit the preceding journal checkpoint first.");
        }

        return row;
    }
}
