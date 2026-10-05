using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Replies;
using IPS.Middleware.Infrastructure.Persistence.Inbound;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.EntityFrameworkCore;

namespace IPS.Middleware.Infrastructure.Repositories.Inbound;

// Reply artifacts are prepared once, then each send attempt is journaled before the call and completed once.
public sealed class IncomingReplyRepository(TransactionDbContext db) : IIncomingReplyRepository
{
    private const string StructuralRejectionReason = "FF01";

    public async Task<IncomingReplySnapshot?> ReadAsync(Guid journalId, CancellationToken token)
    {
        var reply = await db.IncomingReplies
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.JournalId == journalId, token);
        if (reply is null)
        {
            return null;
        }

        var attempts = await db.IncomingReplyAttempts
            .AsNoTracking()
            .Where(x => x.JournalId == journalId)
            .OrderBy(x => x.Number)
            .ToListAsync(token);

        return new IncomingReplySnapshot(
            journalId,
            Envelope(reply),
            reply.UnsignedXml,
            reply.MessageXml,
            reply.MessageKind,
            reply.Status,
            reply.ReviewReason,
            attempts.ConvertAll(attempt => attempt.Snapshot()).AsReadOnly());
    }

    public async Task<IncomingReplyDecision?> ReadDecisionAsync(Guid journalId, CancellationToken token)
    {
        var paymentId = await db.InboundJournal
            .Where(x => x.Id == journalId)
            .Select(x => x.IncomingPaymentId)
            .SingleOrDefaultAsync(token);
        if (paymentId is null)
        {
            return null;
        }

        var payment = await db.IncomingPayments
            .AsNoTracking()
            .SingleAsync(x => x.Id == paymentId, token);
        return payment.IpsDecision is { } decision
            ? new IncomingReplyDecision(decision.Accepted, decision.DecidedAtUtc, decision.ReasonCode, decision.Description)
            : null;
    }

    public async Task StageEnvelopeAsync(InboundClaim claim, IncomingReplyEnvelope envelope, DateTimeOffset now, CancellationToken token)
    {
        var receipt = await OwnedReceiptAsync(claim, now, token);
        var matchesReceipt = receipt.ParticipantBic == envelope.ParticipantBic
            && receipt.OriginalJson is not null
            && IncomingPaymentJson.Read<IncomingPacs008Reference>(receipt.OriginalJson) == envelope.Original;
        if (!matchesReceipt || envelope.MaxAttempts < 1)
        {
            throw new InvalidOperationException("Reply context must match the owned receipt.");
        }

        if (receipt.IncomingPaymentId is null)
        {
            if (envelope.Decision.Accepted || envelope.Decision.ReasonCode != StructuralRejectionReason)
            {
                throw new InvalidOperationException("Only a trusted structural rejection may reply without a payment.");
            }
        }
        else if (await ReadDecisionAsync(claim.JournalId, token) != envelope.Decision)
        {
            throw new InvalidOperationException("Reply must use the payment's immutable IPS decision.");
        }

        if (await db.IncomingReplies.AnyAsync(x => x.JournalId == claim.JournalId, token))
        {
            throw new InvalidOperationException("The receipt already has a reply.");
        }

        db.IncomingReplies.Add(new IncomingReplyRow { JournalId = claim.JournalId, EnvelopeJson = IncomingPaymentJson.Write(envelope) });
    }

    public async Task StageUnsignedAsync(InboundClaim claim, string xml, DateTimeOffset now, CancellationToken token)
    {
        var reply = await OwnedReplyAsync(claim, now, token);
        if (reply.UnsignedXml is not null)
        {
            throw new InvalidOperationException("Unsigned reply is already saved.");
        }

        reply.UnsignedXml = xml;
    }

    public async Task StageMessageAsync(InboundClaim claim, SignedMessage message, DateTimeOffset now, CancellationToken token)
    {
        var reply = await OwnedReplyAsync(claim, now, token);
        if (reply.UnsignedXml is null || reply.MessageXml is not null)
        {
            throw new InvalidOperationException("Reply signing requires unsigned XML and no previous signed artifact.");
        }

        reply.MessageXml = message.Xml;
        reply.MessageKind = message.Kind;
        reply.Status = IncomingReplyStatus.Ready;
    }

    public async Task<IncomingReplyAttempt> StageAttemptAsync(InboundClaim claim, DateTimeOffset now, CancellationToken token)
    {
        var reply = await OwnedReplyAsync(claim, now, token);
        if (reply.Status != IncomingReplyStatus.Ready)
        {
            throw new InvalidOperationException("Only a ready reply can be sent.");
        }

        var attempts = await db.IncomingReplyAttempts
            .Where(x => x.JournalId == claim.JournalId)
            .ToListAsync(token);
        if (attempts.Any(x => x.CompletionJson is not null && !x.Consumed))
        {
            throw new InvalidOperationException("Consume saved response evidence before another send.");
        }

        var number = attempts.Count + 1;
        if (number > Envelope(reply).MaxAttempts)
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
        db.IncomingReplyAttempts.Add(attempt);
        return attempt.Snapshot();
    }

    public async Task StageCompletionAsync(
        InboundClaim claim,
        Guid attemptId,
        ReplyAttemptCompletion completion,
        DateTimeOffset now,
        CancellationToken token)
    {
        var attempt = await OwnedAttemptAsync(claim, attemptId, now, token);
        if (attempt.OwnerToken != claim.Token || attempt.CompletionJson is not null)
        {
            throw new PersistenceConcurrencyException("Only the original live attempt owner can save its response once.");
        }

        if ((completion.Response is null) == (completion.Failure is null))
        {
            throw new ArgumentException("Store either a response or a failure.");
        }

        attempt.CompletionJson = IncomingPaymentJson.Write(completion);
    }

    public async Task StageConsumptionAsync(InboundClaim claim, Guid attemptId, DateTimeOffset now, CancellationToken token)
    {
        var attempt = await OwnedAttemptAsync(claim, attemptId, now, token);
        if (attempt.CompletionJson is null || attempt.Consumed)
        {
            throw new InvalidOperationException("Consume complete evidence only once.");
        }

        attempt.Consumed = true;
    }

    public async Task StageOutcomeAsync(InboundClaim claim, IncomingReplyStatus status, string? reason, DateTimeOffset now, CancellationToken token)
    {
        var reply = await OwnedReplyAsync(claim, now, token);
        var completing = status is IncomingReplyStatus.Delivered or IncomingReplyStatus.ManualReview;
        if (reply.Status != IncomingReplyStatus.Ready || !completing)
        {
            throw new InvalidOperationException("A ready reply may complete or require review once.");
        }

        if (status == IncomingReplyStatus.ManualReview)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        }

        reply.Status = status;
        reply.ReviewReason = status == IncomingReplyStatus.ManualReview ? reason : null;
    }

    public Task<bool> IsOwnerAsync(InboundClaim claim, DateTimeOffset now, CancellationToken token) =>
        db.InboundJournal
            .AsNoTracking()
            .AnyAsync(x => x.Id == claim.JournalId
                && x.Status == InboundProcessingStatus.Pending
                && x.ClaimToken == claim.Token
                && x.ClaimExpiresAtUtc > now, token);

    private async Task<IncomingReplyRow> OwnedReplyAsync(InboundClaim claim, DateTimeOffset now, CancellationToken token)
    {
        await OwnedReceiptAsync(claim, now, token);
        return await db.IncomingReplies.SingleAsync(x => x.JournalId == claim.JournalId, token);
    }

    private async Task<IncomingReplyAttemptRow> OwnedAttemptAsync(InboundClaim claim, Guid attemptId, DateTimeOffset now, CancellationToken token)
    {
        await OwnedReceiptAsync(claim, now, token);
        return await db.IncomingReplyAttempts.SingleAsync(x => x.Id == attemptId && x.JournalId == claim.JournalId, token);
    }

    // Every checkpoint advances the receipt's reply marker, so a stale owner's concurrent write fails on the row version.
    private async Task<InboundJournalEntry> OwnedReceiptAsync(InboundClaim claim, DateTimeOffset now, CancellationToken token)
    {
        var receipt = await db.InboundJournal.FindAsync([claim.JournalId], token);
        if (receipt is null || !receipt.IsOwnedBy(claim, now))
        {
            throw new PersistenceConcurrencyException("Reply checkpoint requires live receipt ownership.");
        }

        var claimToken = db.Entry(receipt).Property(x => x.ClaimToken);
        if (claimToken.OriginalValue != claim.Token || claimToken.IsModified)
        {
            throw new InvalidOperationException("Commit receipt ownership before staging reply checkpoints.");
        }

        receipt.ReplyCheckpoint = Guid.NewGuid();
        return receipt;
    }

    private static IncomingReplyEnvelope Envelope(IncomingReplyRow reply) =>
        IncomingPaymentJson.Read<IncomingReplyEnvelope>(reply.EnvelopeJson);
}
