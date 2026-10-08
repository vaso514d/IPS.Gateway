using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Investigation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Repositories.Payments;

// Each resend journals the original message bytes: ready, submitted once, the response, then the result.
public sealed class ResendRepository(TransactionDbContext db) : IResendRepository
{
    private const string UnresolvedResponse = "Unresolved resend response.";

    public async Task<ResendAttempt?> ReadAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var resend = await db.Resends
            .AsNoTracking()
            .Where(x => x.PaymentId == paymentId)
            .OrderByDescending(x => x.Number)
            .FirstOrDefaultAsync(cancellationToken);
        if (resend is null)
        {
            return null;
        }

        var messages = await db.OutgoingMessages
            .AsNoTracking()
            .Where(x => x.ResendId == resend.Id)
            .ToListAsync(cancellationToken);
        var request = messages.SingleOrDefault(x => x.Direction == OutgoingMessageDirection.Outbound);
        var response = messages.SingleOrDefault(x => x.Direction == OutgoingMessageDirection.Response);
        var result = resend.Outcome is { } outcome
            ? new IpsReply(outcome, PaymentJson.Read<PaymentDetails>(resend.DetailsJson)!)
            : null;

        return new ResendAttempt(
            resend.Id,
            resend.Number,
            resend.InvestigationId,
            resend.DeadlineUtc,
            request?.Snapshot(),
            response?.Snapshot(),
            result,
            resend.TransportFailure);
    }

    // Without an investigation, only an unowned Uncertain payment is due, once it has waited out the first delay.
    public async Task<IReadOnlyList<Guid>> FindDueAsync(DateTimeOffset now, InvestigationOptions options, CancellationToken cancellationToken)
    {
        var firstDue = now - options.FirstDelay;
        return await db.OutgoingMetadata
            .AsNoTracking()
            .Where(x => x.Payment.MessageType != PaymentMessageTypes.Pacs008 && PaymentMessageTypes.Outgoing.Contains(x.Payment.MessageType))
            .Where(x => x.Payment.CurrentStatus == TransactionStatus.Uncertain)
            .Where(x => x.ClaimToken == null || x.ClaimExpiresAtUtc <= now)
            .Where(x => x.NextActionAtUtc == null || x.NextActionAtUtc <= now)
            .Where(x => x.Payment.CurrentSource == StatusSource.Recovery
                || x.Payment.CurrentStatusAtUtc <= firstDue
                || db.Resends.Any(resend => resend.PaymentId == x.Id))
            .OrderBy(x => x.NextActionAtUtc ?? x.Payment.CurrentStatusAtUtc)
            .ThenBy(x => x.Id)
            .Select(x => x.Id)
            .Take(options.DiscoveryBatch)
            .ToListAsync(cancellationToken);
    }

    public void StageAttempt(OutgoingPayment payment, TransactionClaim claim, int number, DateTimeOffset deadlineUtc, DateTimeOffset now)
    {
        RequireOwner(payment, claim, now);
        db.Resends.Add(new ResendRow
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Number = number,
            DeadlineUtc = deadlineUtc.ToUniversalTime(),
            CreatedAtUtc = now.ToUniversalTime()
        });
        db.RequireCurrentVersion(payment);
    }

    public void StageAuthorization(OutgoingPayment payment, TransactionClaim claim, Guid investigationId, int number, DateTimeOffset now)
    {
        RequireOwner(payment, claim, now);
        db.Resends.Add(new ResendRow
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            Number = number,
            InvestigationId = investigationId,
            CreatedAtUtc = now.ToUniversalTime()
        });
        db.RequireCurrentVersion(payment);
    }

    public void StageReady(OutgoingPayment payment, TransactionClaim claim, Guid resendId, DateTimeOffset now)
    {
        RequireOwner(payment, claim, now);
        CommittedResend(payment.Id, resendId);
        var original = OutgoingJournal.Find(db, payment.Id, OutgoingMessageDirection.Outbound)!;
        db.OutgoingMessages.Add(new OutgoingMessageRow
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            ResendId = resendId,
            Direction = OutgoingMessageDirection.Outbound,
            MessageDefinition = original.MessageDefinition,
            Content = original.Content,
            CreatedAtUtc = now.ToUniversalTime(),
            OriginatingMessageId = original.Id,
            Disposition = original.Disposition,
            Status = MessageJournalStatus.ReadyToSend
        });
        db.RequireCurrentVersion(payment);
    }

    public void StageSubmission(OutgoingPayment payment, TransactionClaim claim, Guid resendId, DateTimeOffset now)
    {
        RequireOwner(payment, claim, now);
        var ready = CommittedMessage(resendId, OutgoingMessageDirection.Outbound);
        if (ready.Status != MessageJournalStatus.ReadyToSend)
        {
            throw new InvalidOperationException("A resend may be submitted once.");
        }

        ready.Status = MessageJournalStatus.SendStarted;
        ready.StartedAtUtc = now.ToUniversalTime();
        ready.SubmissionOwner = claim.Token;
        db.RequireCurrentVersion(payment);
    }

    public void StageResponse(OutgoingPayment payment, TransactionClaim claim, Guid resendId, IpsSubmissionResponse response, DateTimeOffset now)
    {
        RequireOwner(payment, claim, now);
        var sent = CommittedMessage(resendId, OutgoingMessageDirection.Outbound);
        var committedSubmission = db.Entry(sent).Property(x => x.Status).OriginalValue == MessageJournalStatus.SendStarted;
        if (!committedSubmission || sent.SubmissionOwner != claim.Token)
        {
            throw new PersistenceConcurrencyException("A response requires the committed submission owner.");
        }

        db.OutgoingMessages.Add(new OutgoingMessageRow
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            ResendId = resendId,
            Direction = OutgoingMessageDirection.Response,
            Content = response.Body,
            CreatedAtUtc = now.ToUniversalTime(),
            OriginatingMessageId = sent.Id,
            Status = MessageJournalStatus.Received,
            HttpStatusCode = response.HttpStatusCode,
            HeadersJson = PaymentJson.Write(response.Headers)
        });
        db.RequireCurrentVersion(payment);
    }

    public void StageResult(
        OutgoingPayment payment,
        TransactionClaim claim,
        Guid resendId,
        IpsReply result,
        string? transportFailure,
        DateTimeOffset now)
    {
        RequireOwner(payment, claim, now);
        var resend = CommittedResend(payment.Id, resendId);
        if (resend.Outcome is not null)
        {
            throw new InvalidOperationException("Resend results are immutable.");
        }

        // A stored response is interpreted; without one, only the workflow's reason is recorded.
        if (Message(resendId, OutgoingMessageDirection.Response) is not null)
        {
            var response = CommittedMessage(resendId, OutgoingMessageDirection.Response);
            if (transportFailure is not null)
            {
                throw new InvalidOperationException("A stored response is interpreted, not replaced by a transport failure.");
            }

            var conclusive = result.Status is IpsReplyStatus.Accepted or IpsReplyStatus.Rejected;
            OutgoingJournal.Consume(response, conclusive, result.Details.Description ?? UnresolvedResponse, now);
        }

        resend.Outcome = result.Status;
        resend.DetailsJson = PaymentJson.Write(result.Details);
        resend.TransportFailure = transportFailure;
        resend.CompletedAtUtc = now.ToUniversalTime();
        db.RequireCurrentVersion(payment);
    }

    private void RequireOwner(OutgoingPayment payment, TransactionClaim claim, DateTimeOffset now) =>
        db.OwnedOutgoing(payment, claim, now, TransactionStatus.Resending);

    private ResendRow CommittedResend(Guid paymentId, Guid resendId)
    {
        var resend = db.Resends.Local.SingleOrDefault(x => x.Id == resendId)
            ?? db.Resends.Single(x => x.Id == resendId);
        if (resend.PaymentId != paymentId || db.Entry(resend).State == EntityState.Added)
        {
            throw new InvalidOperationException("Commit the resend authorization first.");
        }

        return resend;
    }

    private OutgoingMessageRow? Message(Guid resendId, OutgoingMessageDirection direction) =>
        db.OutgoingMessages.Local.SingleOrDefault(x => x.ResendId == resendId && x.Direction == direction)
        ?? db.OutgoingMessages.SingleOrDefault(x => x.ResendId == resendId && x.Direction == direction);

    private OutgoingMessageRow CommittedMessage(Guid resendId, OutgoingMessageDirection direction)
    {
        var row = Message(resendId, direction);
        if (row is null || db.Entry(row).State == EntityState.Added)
        {
            throw new InvalidOperationException("Commit the preceding journal checkpoint first.");
        }

        return row;
    }
}
