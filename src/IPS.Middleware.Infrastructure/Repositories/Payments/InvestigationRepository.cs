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

public sealed class InvestigationRepository(TransactionDbContext db) : IInvestigationRepository
{
    public async Task<InvestigationAttempt?> ReadAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var row = await db.Investigations.AsNoTracking().Where(p => p.PaymentId == paymentId).OrderByDescending(p => p.Number).FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var messages = await db.OutgoingMessages.AsNoTracking().Where(p => p.InvestigationId == row.Id).ToListAsync(cancellationToken);
        return new(new(row.Id, row.Number, row.MessageId, row.StatusRequestId, row.CreatedAtUtc, row.DeadlineUtc), row.UnsignedXml,
            messages.SingleOrDefault(p => p.Direction == OutgoingMessageDirection.Outbound)?.Snapshot(),
            messages.SingleOrDefault(p => p.Direction == OutgoingMessageDirection.Response)?.Snapshot(),
            row.Outcome is { } outcome ? new(outcome, PaymentJson.Read<PaymentDetails>(row.DetailsJson)!) : null, row.TransportFailure);
    }

    public async Task<IReadOnlyList<Guid>> FindDueAsync(DateTimeOffset now, InvestigationOptions options, CancellationToken cancellationToken)
    {
        var firstDue = now - options.FirstDelay;
        return await db.OutgoingMetadata.AsNoTracking().Where(p => p.Payment.MessageType == Pacs008 &&
            (p.Payment.CurrentStatus == TransactionStatus.Uncertain || p.Payment.CurrentStatus == TransactionStatus.Investigating) &&
            (p.ClaimToken == null || p.ClaimExpiresAtUtc <= now) &&
            (p.NextActionAtUtc == null || p.NextActionAtUtc <= now) &&
            (p.Payment.CurrentSource == StatusSource.Recovery || p.Payment.CurrentStatusAtUtc <= firstDue || db.Investigations.Any(i => i.PaymentId == p.Id)) &&
            !db.Investigations.Any(i => i.PaymentId == p.Id && i.Outcome == InvestigationOutcome.NotFound))
            .OrderBy(p => p.NextActionAtUtc ??
                (p.Payment.CurrentSource == StatusSource.Recovery || db.Investigations.Any(i => i.PaymentId == p.Id)
                    ? p.Payment.CurrentStatusAtUtc : p.Payment.CurrentStatusAtUtc.AddSeconds(options.FirstDelay.TotalSeconds))).ThenBy(p => p.Id)
            .Select(p => p.Id).Take(options.DiscoveryBatch).ToListAsync(cancellationToken);
    }

    public void StageIdentity(OutgoingPayment payment, TransactionClaim claim, InvestigationIdentity identity, DateTimeOffset now)
    {
        Own(payment, claim, now);
        if (identity.Id == Guid.Empty || identity.Number < 1 || identity.MessageId.Length is < 1 or > 35 || identity.StatusRequestId.Length is < 1 or > 35)
        {
            throw new ArgumentException("A complete investigation identity is required.");
        }

        var previous = db.Investigations.Where(p => p.PaymentId == payment.Id).OrderByDescending(p => p.Number).FirstOrDefault();
        if (previous is not null && (identity.DeadlineUtc != previous.DeadlineUtc || previous.Outcome != InvestigationOutcome.Unresolved || identity.Number != previous.Number + 1) || previous is null && identity.Number != 1)
        {
            throw new InvalidOperationException("Complete the previous unresolved cycle before creating the next.");
        }

        var row = new InvestigationRow
        {
            Id = identity.Id,
            PaymentId = payment.Id,
            Number = identity.Number,
            MessageId = identity.MessageId,
            StatusRequestId = identity.StatusRequestId,
            CreatedAtUtc = identity.CreatedAtUtc.ToUniversalTime(),
            DeadlineUtc = identity.DeadlineUtc.ToUniversalTime()
        };
        db.Investigations.Add(row);
        db.RequireCurrentVersion(payment);
    }

    public void StageUnsigned(OutgoingPayment payment, TransactionClaim claim, Guid attemptId, string xml, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        Own(payment, claim, now);
        var row = Attempt(payment.Id, attemptId);
        if (row.UnsignedXml is not null || row.Outcome is not null)
        {
            throw new InvalidOperationException("Unsigned preparation is write-once.");
        }

        row.UnsignedXml = xml;
        db.RequireCurrentVersion(payment);
    }

    public void StageReady(OutgoingPayment payment, TransactionClaim claim, Guid attemptId, SignedMessage message, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Xml);
        if (!Enum.IsDefined(message.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(message));
        }

        Own(payment, claim, now);
        var attempt = Attempt(payment.Id, attemptId);
        if (message.Kind == SubmissionMessageKind.DevelopmentUnsigned && message.Xml != attempt.UnsignedXml)
        {
            throw new InvalidOperationException("Development sending must use the frozen unsigned XML.");
        }

        if (db.Entry(attempt).Property(p => p.UnsignedXml).OriginalValue is null || attempt.Outcome is not null || Message(attemptId, OutgoingMessageDirection.Outbound) is not null)
        {
            throw new InvalidOperationException("Commit preparation before selecting exactly one wire message.");
        }

        var original = OutgoingJournal.Find(db, payment.Id, OutgoingMessageDirection.Outbound)
            ?? throw new InvalidOperationException("An investigation requires the original payment message.");
        var row = new OutgoingMessageRow
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            InvestigationId = attemptId,
            Direction = OutgoingMessageDirection.Outbound,
            MessageDefinition = "pacs.028.001.06",
            Content = message.Xml,
            CreatedAtUtc = now.ToUniversalTime(),
            OriginatingMessageId = original.Id,
            Disposition = message.Kind,
            Status = MessageJournalStatus.ReadyToSend
        };
        db.OutgoingMessages.Add(row);
        db.RequireCurrentVersion(payment);
    }

    public void StageSubmission(OutgoingPayment payment, TransactionClaim claim, Guid attemptId, DateTimeOffset now)
    {
        Own(payment, claim, now);
        Attempt(payment.Id, attemptId);
        var row = CommittedMessage(attemptId, OutgoingMessageDirection.Outbound);
        if (row.Status != MessageJournalStatus.ReadyToSend)
        {
            throw new InvalidOperationException("An investigation may be submitted once.");
        }

        row.Status = MessageJournalStatus.SendStarted;
        row.StartedAtUtc = now.ToUniversalTime();
        row.SubmissionOwner = claim.Token;
        db.RequireCurrentVersion(payment);
    }

    public void StageResponse(
        OutgoingPayment payment,
        TransactionClaim claim,
        Guid attemptId,
        IpsSubmissionResponse response,
        DateTimeOffset now)
    {
        Own(payment, claim, now);
        Attempt(payment.Id, attemptId);
        var sent = CommittedMessage(attemptId, OutgoingMessageDirection.Outbound);
        if (db.Entry(sent).Property(p => p.Status).OriginalValue != MessageJournalStatus.SendStarted || sent.SubmissionOwner != claim.Token)
        {
            throw new PersistenceConcurrencyException("A response requires the committed submission owner.");
        }

        if (Message(attemptId, OutgoingMessageDirection.Response) is not null)
        {
            throw new InvalidOperationException("Response evidence is write-once.");
        }

        var row = new OutgoingMessageRow
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
        };
        db.OutgoingMessages.Add(row);
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
        Own(payment, claim, now);
        var attempt = Attempt(payment.Id, attemptId);
        if (attempt.Outcome is not null)
        {
            throw new InvalidOperationException("Investigation results are immutable.");
        }

        if (Message(attemptId, OutgoingMessageDirection.Response) is { } response)
        {
            CommittedMessage(attemptId, OutgoingMessageDirection.Response);
            if (response.Status != MessageJournalStatus.Received || transportFailure is not null)
            {
                throw new InvalidOperationException("Interpret unconsumed response evidence only.");
            }

            response.Status = result.Outcome == InvestigationOutcome.Unresolved ? MessageJournalStatus.Failed : MessageJournalStatus.Processed;
            response.ProcessedAtUtc = now.ToUniversalTime();
            response.MessageDefinition = result.Outcome == InvestigationOutcome.Unresolved ? null : "pacs.002.001.14";
            response.Failure = result.Outcome == InvestigationOutcome.Unresolved ? result.Details.Description ?? "Unresolved investigation response." : null;
            db.RequireCurrentVersion(payment);
        }
        else if (result.Outcome != InvestigationOutcome.Unresolved || string.IsNullOrWhiteSpace(transportFailure) ||
            db.Entry(CommittedMessage(attemptId, OutgoingMessageDirection.Outbound)).Property(p => p.Status).OriginalValue != MessageJournalStatus.SendStarted)
        {
            throw new InvalidOperationException("A result requires committed response or submission failure evidence.");
        }

        attempt.Outcome = result.Outcome;
        attempt.DetailsJson = PaymentJson.Write(result.Details);
        attempt.TransportFailure = transportFailure;
        attempt.CompletedAtUtc = now.ToUniversalTime();
        db.RequireCurrentVersion(payment);
    }

    private void Own(OutgoingPayment payment, TransactionClaim claim, DateTimeOffset now)
    {
        var entry = db.Entry(db.Metadata(payment));
        if (entry.State is EntityState.Detached or EntityState.Added || payment.MessageType != Pacs008 || payment.CurrentStatus != TransactionStatus.Investigating)
        {
            throw new InvalidOperationException("Investigation writes require a tracked investigating payment.");
        }

        if (!entry.Entity.HasLiveClaim(claim, now) || entry.Property(p => p.ClaimToken).OriginalValue != claim.Token)
        {
            throw new PersistenceConcurrencyException("Investigation requires a committed live owner.");
        }
    }
    private InvestigationRow Attempt(Guid paymentId, Guid id)
    {
        var row = db.Investigations.Local.SingleOrDefault(p => p.Id == id) ?? db.Investigations.Single(p => p.Id == id);
        if (row.PaymentId != paymentId || db.Entry(row).State == EntityState.Added)
        {
            throw new InvalidOperationException("Commit the investigation identity first.");
        }

        return row;
    }
    private OutgoingMessageRow? Message(Guid id, OutgoingMessageDirection direction) =>
        db.OutgoingMessages.Local.SingleOrDefault(p => p.InvestigationId == id && p.Direction == direction) ?? db.OutgoingMessages.SingleOrDefault(p => p.InvestigationId == id && p.Direction == direction);
    private OutgoingMessageRow CommittedMessage(Guid id, OutgoingMessageDirection direction)
    {
        var row = Message(id, direction);
        if (row is null || db.Entry(row).State == EntityState.Added)
        {
            throw new InvalidOperationException("Commit the preceding journal checkpoint first.");
        }

        return row;
    }
}
