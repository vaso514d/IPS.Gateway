using System.Linq.Expressions;
using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Payments.Investigation;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;
using IPS.Middleware.Infrastructure.Persistence;
using IPS.Middleware.Infrastructure.Persistence.Outgoing;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;
using static IPS.Middleware.Infrastructure.Persistence.PaymentColumns;

namespace IPS.Middleware.Infrastructure.Repositories.Payments;

// Each investigation cycle journals its pacs.028 like a payment: prepare, submit once, record the response, then the result.
public sealed class InvestigationRepository(TransactionDbContext db) : IInvestigationRepository
{
    private const string Pacs028Definition = "pacs.028.001.06";
    private const string Pacs002Definition = "pacs.002.001.14";
    private const string UnresolvedResponse = "Unresolved investigation response.";

    public async Task<InvestigationAttempt?> ReadAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var attempt = await db.Investigations
            .AsNoTracking()
            .Where(x => x.PaymentId == paymentId)
            .OrderByDescending(x => x.Number)
            .FirstOrDefaultAsync(cancellationToken);
        if (attempt is null)
        {
            return null;
        }

        var messages = await db.OutgoingMessages
            .AsNoTracking()
            .Where(x => x.InvestigationId == attempt.Id)
            .ToListAsync(cancellationToken);
        var request = messages.SingleOrDefault(x => x.Direction == OutgoingMessageDirection.Outbound);
        var response = messages.SingleOrDefault(x => x.Direction == OutgoingMessageDirection.Response);
        var result = attempt.Outcome is { } outcome
            ? new InvestigationReply(outcome, PaymentJson.Read<PaymentDetails>(attempt.DetailsJson)!)
            : null;

        return new InvestigationAttempt(
            new InvestigationIdentity(attempt.Id, attempt.Number, attempt.MessageId, attempt.StatusRequestId, attempt.CreatedAtUtc, attempt.DeadlineUtc),
            attempt.UnsignedXml,
            request?.Snapshot(),
            response?.Snapshot(),
            result,
            attempt.TransportFailure);
    }

    public async Task<IReadOnlyList<Guid>> FindDueAsync(DateTimeOffset now, InvestigationOptions options, CancellationToken cancellationToken)
    {
        var firstDue = now - options.FirstDelay;
        return await db.OutgoingMetadata
            .AsNoTracking()
            .Where(IsUnresolvedPacs008())
            .Where(IsUnownedAndDue(now))
            .Where(HasWaitedForFirstInvestigation(firstDue))
            .Where(x => !db.Investigations.Any(i => i.PaymentId == x.Id && i.Outcome == InvestigationOutcome.NotFound))
            // Without a scheduled action, a first cycle becomes due FirstDelay after the uncertain outcome.
            .OrderBy(x => x.NextActionAtUtc
                ?? (x.Payment.CurrentSource == StatusSource.Recovery || db.Investigations.Any(i => i.PaymentId == x.Id)
                    ? x.Payment.CurrentStatusAtUtc
                    : x.Payment.CurrentStatusAtUtc.AddSeconds(options.FirstDelay.TotalSeconds)))
            .ThenBy(x => x.Id)
            .Select(x => x.Id)
            .Take(options.DiscoveryBatch)
            .ToListAsync(cancellationToken);
    }

    public void StageIdentity(OutgoingPayment payment, TransactionClaim claim, InvestigationIdentity identity, DateTimeOffset now)
    {
        RequireOwner(payment, claim, now);
        var attempt = new InvestigationRow
        {
            Id = identity.Id,
            PaymentId = payment.Id,
            Number = identity.Number,
            MessageId = identity.MessageId,
            StatusRequestId = identity.StatusRequestId,
            CreatedAtUtc = identity.CreatedAtUtc.ToUniversalTime(),
            DeadlineUtc = identity.DeadlineUtc.ToUniversalTime()
        };
        db.Investigations.Add(attempt);
        db.RequireCurrentVersion(payment);
    }

    public void StageUnsigned(OutgoingPayment payment, TransactionClaim claim, Guid attemptId, string xml, DateTimeOffset now)
    {
        RequireOwner(payment, claim, now);
        var attempt = CommittedAttempt(payment.Id, attemptId);
        if (attempt.UnsignedXml is not null || attempt.Outcome is not null)
        {
            throw new InvalidOperationException("Unsigned preparation is write-once.");
        }

        attempt.UnsignedXml = xml;
        db.RequireCurrentVersion(payment);
    }

    public void StageReady(OutgoingPayment payment, TransactionClaim claim, Guid attemptId, SignedMessage message, DateTimeOffset now)
    {
        RequireOwner(payment, claim, now);
        var attempt = CommittedAttempt(payment.Id, attemptId);
        if (message.Kind == SubmissionMessageKind.DevelopmentUnsigned && message.Xml != attempt.UnsignedXml)
        {
            throw new InvalidOperationException("Development sending must use the frozen unsigned XML.");
        }

        var preparedAndOpen = db.Entry(attempt).Property(x => x.UnsignedXml).OriginalValue is not null
            && attempt.Outcome is null
            && Message(attemptId, OutgoingMessageDirection.Outbound) is null;
        if (!preparedAndOpen)
        {
            throw new InvalidOperationException("Commit preparation before selecting exactly one wire message.");
        }

        var original = OutgoingJournal.Find(db, payment.Id, OutgoingMessageDirection.Outbound)
            ?? throw new InvalidOperationException("An investigation requires the original payment message.");
        db.OutgoingMessages.Add(new OutgoingMessageRow
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            InvestigationId = attemptId,
            Direction = OutgoingMessageDirection.Outbound,
            MessageDefinition = Pacs028Definition,
            Content = message.Xml,
            CreatedAtUtc = now.ToUniversalTime(),
            OriginatingMessageId = original.Id,
            Disposition = message.Kind,
            Status = MessageJournalStatus.ReadyToSend
        });
        db.RequireCurrentVersion(payment);
    }

    public void StageSubmission(OutgoingPayment payment, TransactionClaim claim, Guid attemptId, DateTimeOffset now)
    {
        RequireOwner(payment, claim, now);
        CommittedAttempt(payment.Id, attemptId);
        var ready = CommittedMessage(attemptId, OutgoingMessageDirection.Outbound);
        if (ready.Status != MessageJournalStatus.ReadyToSend)
        {
            throw new InvalidOperationException("An investigation may be submitted once.");
        }

        ready.Status = MessageJournalStatus.SendStarted;
        ready.StartedAtUtc = now.ToUniversalTime();
        ready.SubmissionOwner = claim.Token;
        db.RequireCurrentVersion(payment);
    }

    public void StageResponse(OutgoingPayment payment, TransactionClaim claim, Guid attemptId, IpsSubmissionResponse response, DateTimeOffset now)
    {
        RequireOwner(payment, claim, now);
        CommittedAttempt(payment.Id, attemptId);
        var sent = CommittedMessage(attemptId, OutgoingMessageDirection.Outbound);
        var committedSubmission = db.Entry(sent).Property(x => x.Status).OriginalValue == MessageJournalStatus.SendStarted;
        if (!committedSubmission || sent.SubmissionOwner != claim.Token)
        {
            throw new PersistenceConcurrencyException("A response requires the committed submission owner.");
        }

        if (Message(attemptId, OutgoingMessageDirection.Response) is not null)
        {
            throw new InvalidOperationException("Response evidence is write-once.");
        }

        db.OutgoingMessages.Add(new OutgoingMessageRow
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            InvestigationId = attemptId,
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
        Guid attemptId,
        InvestigationReply result,
        string? transportFailure,
        DateTimeOffset now)
    {
        RequireOwner(payment, claim, now);
        var attempt = CommittedAttempt(payment.Id, attemptId);
        if (attempt.Outcome is not null)
        {
            throw new InvalidOperationException("Investigation results are immutable.");
        }

        if (Message(attemptId, OutgoingMessageDirection.Response) is not null)
        {
            ConsumeResponse(attemptId, result, transportFailure, now);
        }
        else
        {
            RequireAbandonedSubmission(attemptId, result, transportFailure);
        }

        attempt.Outcome = result.Outcome;
        attempt.DetailsJson = PaymentJson.Write(result.Details);
        attempt.TransportFailure = transportFailure;
        attempt.CompletedAtUtc = now.ToUniversalTime();
        db.RequireCurrentVersion(payment);
    }

    private void ConsumeResponse(Guid attemptId, InvestigationReply result, string? transportFailure, DateTimeOffset now)
    {
        var response = CommittedMessage(attemptId, OutgoingMessageDirection.Response);
        if (response.Status != MessageJournalStatus.Received || transportFailure is not null)
        {
            throw new InvalidOperationException("Interpret unconsumed response evidence only.");
        }

        var unresolved = result.Outcome == InvestigationOutcome.Unresolved;
        response.Status = unresolved ? MessageJournalStatus.Failed : MessageJournalStatus.Processed;
        response.ProcessedAtUtc = now.ToUniversalTime();
        response.MessageDefinition = unresolved ? null : Pacs002Definition;
        response.Failure = unresolved ? result.Details.Description ?? UnresolvedResponse : null;
    }

    // Without a response, only an unresolved result for a committed, abandoned submission may be recorded.
    private void RequireAbandonedSubmission(Guid attemptId, InvestigationReply result, string? transportFailure)
    {
        var sent = CommittedMessage(attemptId, OutgoingMessageDirection.Outbound);
        var submissionStarted = db.Entry(sent).Property(x => x.Status).OriginalValue == MessageJournalStatus.SendStarted;
        var abandoned = result.Outcome == InvestigationOutcome.Unresolved
            && !string.IsNullOrWhiteSpace(transportFailure)
            && submissionStarted;
        if (!abandoned)
        {
            throw new InvalidOperationException("A result requires committed response or submission failure evidence.");
        }
    }

    private static Expression<Func<OutgoingPaymentMetadata, bool>> IsUnresolvedPacs008() =>
        x => x.Payment.MessageType == Pacs008
            && (x.Payment.CurrentStatus == TransactionStatus.Uncertain || x.Payment.CurrentStatus == TransactionStatus.Investigating);

    private static Expression<Func<OutgoingPaymentMetadata, bool>> IsUnownedAndDue(DateTimeOffset now) =>
        x => (x.ClaimToken == null || x.ClaimExpiresAtUtc <= now)
            && (x.NextActionAtUtc == null || x.NextActionAtUtc <= now);

    // Recovery and already-started investigations continue at once; a fresh uncertain outcome waits FirstDelay.
    private Expression<Func<OutgoingPaymentMetadata, bool>> HasWaitedForFirstInvestigation(DateTimeOffset firstDue) =>
        x => x.Payment.CurrentSource == StatusSource.Recovery
            || x.Payment.CurrentStatusAtUtc <= firstDue
            || db.Investigations.Any(i => i.PaymentId == x.Id);

    private void RequireOwner(OutgoingPayment payment, TransactionClaim claim, DateTimeOffset now) =>
        db.OwnedPacs008(payment, claim, now, TransactionStatus.Investigating);

    private InvestigationRow CommittedAttempt(Guid paymentId, Guid attemptId)
    {
        var attempt = db.Investigations.Local.SingleOrDefault(x => x.Id == attemptId)
            ?? db.Investigations.Single(x => x.Id == attemptId);
        if (attempt.PaymentId != paymentId || db.Entry(attempt).State == EntityState.Added)
        {
            throw new InvalidOperationException("Commit the investigation identity first.");
        }

        return attempt;
    }

    private OutgoingMessageRow? Message(Guid attemptId, OutgoingMessageDirection direction) =>
        db.OutgoingMessages.Local.SingleOrDefault(x => x.InvestigationId == attemptId && x.Direction == direction)
        ?? db.OutgoingMessages.SingleOrDefault(x => x.InvestigationId == attemptId && x.Direction == direction);

    private OutgoingMessageRow CommittedMessage(Guid attemptId, OutgoingMessageDirection direction)
    {
        var row = Message(attemptId, direction);
        if (row is null || db.Entry(row).State == EntityState.Added)
        {
            throw new InvalidOperationException("Commit the preceding journal checkpoint first.");
        }

        return row;
    }
}
