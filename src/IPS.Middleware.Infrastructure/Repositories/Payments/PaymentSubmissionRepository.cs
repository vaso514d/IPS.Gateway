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

// The submission marker commits before any remote call; the response and its interpretation are separate checkpoints.
public sealed class PaymentSubmissionRepository(TransactionDbContext db) : IPaymentSubmissionRepository
{
    private const string Pacs002Definition = "pacs.002.001.14";
    private const string InconclusiveResponse = "The response did not establish a valid correlated final outcome.";

    public async Task<IReadOnlyList<OutgoingMessage>> ReadJournalAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var rows = await db.OutgoingMessages
            .AsNoTracking()
            .Where(x => x.PaymentId == paymentId)
            .OrderBy(x => x.Direction)
            .ToListAsync(cancellationToken);
        return rows.ConvertAll(row => row.Snapshot()).AsReadOnly();
    }

    public async Task<PaymentSubmission?> ReadAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        if (!await db.Payments.AnyAsync(x => x.Id == paymentId && x.MessageType == Pacs008, cancellationToken))
        {
            return null;
        }

        var journal = await ReadJournalAsync(paymentId, cancellationToken);
        var submitted = journal.SingleOrDefault(x => x.InvestigationId == null && x.Direction == OutgoingMessageDirection.Outbound);
        var received = journal.SingleOrDefault(x => x.InvestigationId == null && x.Direction == OutgoingMessageDirection.Response);
        return new PaymentSubmission(submitted?.Submission, received?.Response);
    }

    public void StageSubmission(OutgoingPayment payment, TransactionClaim claim, SubmissionMessageKind messageKind, DateTimeOffset now)
    {
        db.OwnedPacs008(payment, claim, now, TransactionStatus.Sending);
        var ready = CommittedMessage(payment.Id, OutgoingMessageDirection.Outbound);
        if (ready.Status != MessageJournalStatus.ReadyToSend || ready.Disposition != messageKind)
        {
            throw new InvalidOperationException("The committed message must be ready and must match the selected disposition.");
        }

        ready.Status = MessageJournalStatus.SendStarted;
        ready.StartedAtUtc = now.ToUniversalTime();
        ready.SubmissionOwner = claim.Token;
        db.RequireCurrentVersion(payment);
    }

    public void StageResponse(OutgoingPayment payment, TransactionClaim claim, IpsSubmissionResponse response, DateTimeOffset now)
    {
        db.OwnedPacs008(payment, claim, now, TransactionStatus.Sending);
        var sent = CommittedMessage(payment.Id, OutgoingMessageDirection.Outbound);
        if (db.Entry(sent).Property(x => x.Status).OriginalValue != MessageJournalStatus.SendStarted)
        {
            throw new InvalidOperationException("Commit submission before recording its response.");
        }

        if (sent.SubmissionOwner != claim.Token)
        {
            throw new PersistenceConcurrencyException("The response belongs to a different submission owner.");
        }

        var headersJson = PaymentJson.Write(response.Headers);
        if (OutgoingJournal.Find(db, payment.Id, OutgoingMessageDirection.Response) is { } existing)
        {
            var same = existing.Content == response.Body
                && existing.HttpStatusCode == response.HttpStatusCode
                && existing.HeadersJson == headersJson;
            if (!same)
            {
                throw new InvalidOperationException("Response evidence cannot be replaced.");
            }

            return;
        }

        db.OutgoingMessages.Add(new OutgoingMessageRow
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Direction = OutgoingMessageDirection.Response,
            Content = response.Body,
            CreatedAtUtc = now.ToUniversalTime(),
            OriginatingMessageId = sent.Id,
            Status = MessageJournalStatus.Received,
            HttpStatusCode = response.HttpStatusCode,
            HeadersJson = headersJson
        });
        db.RequireCurrentVersion(payment);
    }

    public void StageInterpretation(OutgoingPayment payment, TransactionClaim claim, IpsReply reply, DateTimeOffset now)
    {
        db.OwnedPacs008(payment, claim, now, TransactionStatus.Sending);
        var received = CommittedMessage(payment.Id, OutgoingMessageDirection.Response);
        if (received.Status != MessageJournalStatus.Received)
        {
            throw new InvalidOperationException("Only unconsumed response evidence can be interpreted.");
        }

        // Only a validated, correlated response establishes its protocol definition.
        var conclusive = reply.Status is IpsReplyStatus.Accepted or IpsReplyStatus.Rejected;
        received.Status = conclusive ? MessageJournalStatus.Processed : MessageJournalStatus.Failed;
        received.MessageDefinition = conclusive ? Pacs002Definition : null;
        received.ProcessedAtUtc = now.ToUniversalTime();
        received.Failure = conclusive ? null : reply.Details.Description ?? InconclusiveResponse;
        db.RequireCurrentVersion(payment);
    }

    private OutgoingMessageRow CommittedMessage(Guid paymentId, OutgoingMessageDirection direction)
    {
        var row = OutgoingJournal.Find(db, paymentId, direction);
        if (row is null || db.Entry(row).State == EntityState.Added)
        {
            throw new InvalidOperationException("Commit the preceding journal checkpoint first.");
        }

        return row;
    }
}
