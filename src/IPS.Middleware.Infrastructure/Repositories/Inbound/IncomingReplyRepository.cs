using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Replies;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

public sealed class IncomingReplyRepository(TransactionDbContext db) : IIncomingReplyRepository
{
    public async Task<IncomingReplySnapshot?> ReadAsync(Guid journalId, CancellationToken token)
    {
        var row = await db.IncomingReplies.AsNoTracking().SingleOrDefaultAsync(r => r.JournalId == journalId, token);
        if (row is null)
        {
            return null;
        }

        var attempts = await db.IncomingReplyAttempts.AsNoTracking().Where(a => a.JournalId == journalId).OrderBy(a => a.Number).ToListAsync(token);
        return new(journalId, Envelope(row), row.UnsignedXml, row.MessageXml, row.MessageKind, row.Status, row.ReviewReason,
            attempts.ConvertAll(a => a.Snapshot()).AsReadOnly());
    }

    public async Task<IncomingReplyDecision?> ReadDecisionAsync(Guid journalId, CancellationToken token)
    {
        var paymentId = await db.InboundJournal.Where(r => r.Id == journalId).Select(r => r.IncomingPaymentId).SingleOrDefaultAsync(token);
        if (paymentId is null)
        {
            return null;
        }

        var p = await db.IncomingPayments.AsNoTracking().SingleAsync(p => p.Id == paymentId, token);
        return p.IpsDecision is { } d ? new(d.Accepted, d.DecidedAtUtc, d.ReasonCode, d.Description) : null;
    }

    public async Task StageEnvelopeAsync(InboundClaim claim, IncomingReplyEnvelope envelope, DateTimeOffset now, CancellationToken token)
    {
        var receipt = await TouchAsync(claim, now, token);
        if (receipt.ParticipantBic != envelope.ParticipantBic || receipt.OriginalJson is null ||
            IncomingPaymentJson.Read<IncomingPacs008Reference>(receipt.OriginalJson) != envelope.Original || envelope.MaxAttempts < 1)
        {
            throw new InvalidOperationException("Reply context must match the owned receipt.");
        }

        if (receipt.IncomingPaymentId is not null)
        {
            if (await ReadDecisionAsync(claim.JournalId, token) != envelope.Decision)
            {
                throw new InvalidOperationException("Reply must use the payment's immutable IPS decision.");
            }
        }
        else if (envelope.Decision.Accepted || envelope.Decision.ReasonCode != "FF01")
        {
            throw new InvalidOperationException("Only a trusted structural rejection may reply without a payment.");
        }

        if (await db.IncomingReplies.AnyAsync(r => r.JournalId == claim.JournalId, token))
        {
            throw new InvalidOperationException("The receipt already has a reply.");
        }

        db.Add(new IncomingReplyRow { JournalId = claim.JournalId, EnvelopeJson = IncomingPaymentJson.Write(envelope) });
    }

    public async Task StageUnsignedAsync(InboundClaim claim, string xml, DateTimeOffset now, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        var row = await OwnedReplyAsync(claim, now, token);
        if (row.UnsignedXml is not null)
        {
            throw new InvalidOperationException("Unsigned reply is already saved.");
        }

        row.UnsignedXml = xml;
    }
    public async Task StageMessageAsync(InboundClaim claim, SignedMessage message, DateTimeOffset now, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message.Xml);
        var row = await OwnedReplyAsync(claim, now, token);
        if (row.UnsignedXml is null || row.MessageXml is not null || !Enum.IsDefined(message.Kind))
        {
            throw new InvalidOperationException("Reply signing requires unsigned XML and no previous signed artifact.");
        }

        row.MessageXml = message.Xml;
        row.MessageKind = message.Kind;
        row.Status = IncomingReplyStatus.Ready;
    }
    public async Task<IncomingReplyAttempt> StageAttemptAsync(InboundClaim claim, DateTimeOffset now, CancellationToken token)
    {
        var row = await OwnedReplyAsync(claim, now, token);
        if (row.Status != IncomingReplyStatus.Ready)
        {
            throw new InvalidOperationException("Only a ready reply can be sent.");
        }

        var attempts = await db.IncomingReplyAttempts.Where(a => a.JournalId == claim.JournalId).ToListAsync(token);
        if (attempts.Any(a => a.CompletionJson is not null && !a.Consumed))
        {
            throw new InvalidOperationException("Consume saved response evidence before another send.");
        }

        var number = attempts.Count + 1;
        if (number > Envelope(row).MaxAttempts)
        {
            throw new InvalidOperationException("Reply attempt budget is exhausted.");
        }

        var attempt = new IncomingReplyAttemptRow
        {
            Id = Guid.NewGuid(),
            JournalId = claim.JournalId,
            Number = number,
            OwnerToken = claim.Token,
            StartedAtUtc = now.ToUniversalTime()
        };
        db.Add(attempt);
        return attempt.Snapshot();
    }
    public async Task StageCompletionAsync(
        InboundClaim claim,
        Guid attemptId,
        ReplyAttemptCompletion completion,
        DateTimeOffset now,
        CancellationToken token)
    {
        var call = await OwnedAttemptAsync(claim, attemptId, now, token);
        if (call.OwnerToken != claim.Token || call.CompletionJson is not null)
        {
            throw new PersistenceConcurrencyException("Only the original live attempt owner can save its response once.");
        }

        if ((completion.Response is null) == (completion.Failure is null))
        {
            throw new ArgumentException("Store either a response or a failure.");
        }

        call.CompletionJson = IncomingPaymentJson.Write(completion);
    }
    public async Task StageConsumptionAsync(InboundClaim claim, Guid attemptId, DateTimeOffset now, CancellationToken token)
    {
        var call = await OwnedAttemptAsync(claim, attemptId, now, token);
        if (call.CompletionJson is null || call.Consumed)
        {
            throw new InvalidOperationException("Consume complete evidence only once.");
        }

        call.Consumed = true;
    }
    public async Task StageOutcomeAsync(InboundClaim claim, IncomingReplyStatus status, string? reason, DateTimeOffset now, CancellationToken token)
    {
        var row = await OwnedReplyAsync(claim, now, token);
        if (row.Status != IncomingReplyStatus.Ready || status is not (IncomingReplyStatus.Delivered or IncomingReplyStatus.ManualReview))
        {
            throw new InvalidOperationException("A ready reply may complete or require review once.");
        }

        if (status == IncomingReplyStatus.ManualReview)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        }

        row.Status = status;
        row.ReviewReason = status == IncomingReplyStatus.ManualReview ? reason : null;
    }
    public async Task<bool> IsOwnerAsync(InboundClaim claim, DateTimeOffset now, CancellationToken token)
    {
        return await db.InboundJournal.AsNoTracking().AnyAsync(r => r.Id == claim.JournalId && r.Status == InboundProcessingStatus.Pending &&
            r.ClaimToken == claim.Token && r.ClaimExpiresAtUtc > now, token);
    }
    private async Task<IncomingReplyRow> OwnedReplyAsync(InboundClaim claim, DateTimeOffset now, CancellationToken token)
    {
        await TouchAsync(claim, now, token);
        return await db.IncomingReplies.SingleAsync(r => r.JournalId == claim.JournalId, token);
    }
    private async Task<InboundJournalEntry> TouchAsync(InboundClaim claim, DateTimeOffset now, CancellationToken token)
    {
        var receipt = await db.InboundJournal.FindAsync([claim.JournalId], token);
        if (receipt is null || !receipt.IsOwnedBy(claim, now))
        {
            throw new PersistenceConcurrencyException("Reply checkpoint requires live receipt ownership.");
        }

        var entry = db.Entry(receipt);
        if (entry.Property(r => r.ClaimToken).OriginalValue != claim.Token || entry.Property(r => r.ClaimToken).IsModified)
        {
            throw new InvalidOperationException("Commit receipt ownership before staging reply checkpoints.");
        }

        receipt.ReplyCheckpoint = Guid.NewGuid();
        return receipt;
    }
    private async Task<IncomingReplyAttemptRow> OwnedAttemptAsync(InboundClaim claim, Guid id, DateTimeOffset now, CancellationToken token)
    {
        await TouchAsync(claim, now, token);
        return await db.IncomingReplyAttempts.SingleAsync(a => a.Id == id && a.JournalId == claim.JournalId, token);
    }
    private static IncomingReplyEnvelope Envelope(IncomingReplyRow row) => IncomingPaymentJson.Read<IncomingReplyEnvelope>(row.EnvelopeJson);
}
