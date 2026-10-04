using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Inbound.Replies;

/// <summary>Prepares a receipt's immutable reply, then performs one durable send attempt per invocation.</summary>
public sealed class IncomingReplyProcessing(IInboundReceiptRepository receipts, IInboundWorkRepository work,
    IIncomingReplyRepository replies, IUnitOfWork unit, IIncomingReplyProtocol protocol, IIncomingReplyClient client,
    Pacs008ProtocolProfile profile, IncomingReplyOptions options, TimeProvider timeProvider)
{
    private const string Exhausted = "Reply attempt budget exhausted without a conclusive IPS result.";

    public async Task ProcessAsync(Guid journalId, CancellationToken cancellationToken)
    {
        try
        {
            var claim = await work.StageClaimAsync(journalId, Now, options.Ownership, cancellationToken);
            if (claim is null) return;
            await unit.SaveAsync(cancellationToken);
            var reply = await replies.ReadAsync(journalId, cancellationToken) ?? await PrepareEnvelopeAsync(claim, cancellationToken);
            if (reply is null) return;
            if (reply.Status is IncomingReplyStatus.Delivered or IncomingReplyStatus.ManualReview)
                throw new InvalidOperationException("A terminal reply must not have a pending receipt.");
            if (reply.UnsignedXml is null)
            {
                await replies.StageUnsignedAsync(claim, protocol.Build(reply.Envelope), Now, cancellationToken);
                reply = await SaveAndReadAsync(claim, cancellationToken);
            }
            if (reply.MessageXml is null)
            {
                if (await protocol.SignAsync(reply.UnsignedXml!, cancellationToken) is not SignedMessage signed)
                {
                    await ReleaseAsync(claim, Now + options.PreparationRetryDelay, cancellationToken);
                    return;
                }
                await replies.StageMessageAsync(claim, signed, Now, cancellationToken);
                reply = await SaveAndReadAsync(claim, cancellationToken);
            }
            foreach (var saved in reply.Attempts.Where(a => a.Completion is not null && !a.Consumed))
                if (await InterpretAsync(claim, reply.Envelope, saved, cancellationToken)) return;
            if (reply.Attempts.Count >= reply.Envelope.MaxAttempts)
            {
                await FinishAsync(claim, IncomingReplyStatus.ManualReview, Exhausted, cancellationToken);
                return;
            }
            // Recovery respects the durable attempt time; a restart cannot skip the retry delay.
            DateTimeOffset? due = reply.Attempts.LastOrDefault() is { } last ? (last.Completion?.ObservedAtUtc ?? last.StartedAtUtc) + options.RetryDelay : null;
            if (due > Now)
            {
                await ReleaseAsync(claim, due.Value, cancellationToken);
                return;
            }
            var attempt = await replies.StageAttemptAsync(claim, Now, cancellationToken);
            await unit.SaveAsync(cancellationToken);
            if (!await replies.IsOwnerAsync(claim, Now, cancellationToken)) return;
            cancellationToken.ThrowIfCancellationRequested();
            // No database transaction spans the send. Clamp to remaining ownership, not the original payment deadline.
            var budget = TimeSpan.FromTicks(Math.Min(options.CallTimeout.Ticks, (claim.ExpiresAtUtc - Now - options.PersistenceBudget).Ticks));
            if (budget <= TimeSpan.Zero) return;
            var completion = await SendAsync(reply, budget, cancellationToken);
            using var persistence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
            await replies.StageCompletionAsync(claim, attempt.Id, completion, Now, persistence.Token);
            await unit.SaveAsync(persistence.Token);
            cancellationToken.ThrowIfCancellationRequested();
            if (await InterpretAsync(claim, reply.Envelope, attempt with { Completion = completion }, cancellationToken)) return;
            if (attempt.Number >= reply.Envelope.MaxAttempts)
                await FinishAsync(claim, IncomingReplyStatus.ManualReview, Exhausted, cancellationToken);
            else await ReleaseAsync(claim, Now + options.RetryDelay, cancellationToken);
        }
        catch (PersistenceConcurrencyException) { } // Dispose this scope; a newer owner or receipt update won.
    }

    /// <summary>Freezes the reply context once; null when the receipt is held or its payment decision is not yet committed.</summary>
    private async Task<IncomingReplySnapshot?> PrepareEnvelopeAsync(InboundClaim claim, CancellationToken token)
    {
        var receipt = (await receipts.ReadAsync(claim.JournalId, token))!.Receipt;
        if (receipt.MessageType is not (PaymentMessageTypes.Pacs008 or "pacs.008.001.12"))
        {
            await HoldAsync(claim, "Unsupported reply message type.", token);
            return null;
        }
        IncomingPacs008Reference original;
        IncomingReplyDecision? decision;
        switch (protocol.Read(receipt.RawXml))
        {
            case IncomingPacs008ReadResult.Reject rejected:
                original = rejected.Original;
                decision = new(false, Now, rejected.ReasonCode, rejected.Description);
                break;
            case IncomingPacs008ReadResult.Ready ready:
                original = ready.Payment.Original;
                decision = await replies.ReadDecisionAsync(claim.JournalId, token);
                if (decision is null)
                {
                    await ReleaseAsync(claim, Now + options.PreparationRetryDelay, token);
                    return null;
                }
                break;
            case IncomingPacs008ReadResult.Hold:
                await HoldAsync(claim, "Untrusted or unsupported incoming payment.", token);
                return null;
            default: throw new InvalidOperationException("Unsupported protocol result.");
        }
        await work.StageOriginalReferencesAsync(claim, original, Now, token);
        var context = new IncomingReplyContext(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), Now);
        await replies.StageEnvelopeAsync(claim, new(receipt.ParticipantBic, original, decision, context, profile, options.MaxAttempts), Now, token);
        return await SaveAndReadAsync(claim, token);
    }

    private async Task<ReplyAttemptCompletion> SendAsync(IncomingReplySnapshot reply, TimeSpan budget, CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(budget, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        try { return new(await client.SendAsync(reply.Envelope.ParticipantBic, reply.MessageXml!, linked.Token).WaitAsync(linked.Token), null, Now); }
        catch (Exception error) { return new(null, $"{error.GetType().Name}: {error.Message}", Now); }
    }

    /// <summary>Consumes saved evidence; true when it settled the reply and the receipt.</summary>
    private async Task<bool> InterpretAsync(InboundClaim claim, IncomingReplyEnvelope envelope, IncomingReplyAttempt attempt, CancellationToken token)
    {
        var result = protocol.Interpret(attempt.Completion!, envelope);
        await replies.StageConsumptionAsync(claim, attempt.Id, Now, token);
        switch (result.Outcome)
        {
            case ReplyDeliveryOutcome.Delivered: await FinishAsync(claim, IncomingReplyStatus.Delivered, null, token); return true;
            case ReplyDeliveryOutcome.Conflict: await FinishAsync(claim, IncomingReplyStatus.ManualReview, result.Description, token); return true;
            default: await unit.SaveAsync(token); return false;
        }
    }

    private async Task FinishAsync(InboundClaim claim, IncomingReplyStatus status, string? reason, CancellationToken token)
    {
        await replies.StageOutcomeAsync(claim, status, reason, Now, token);
        await CommitAsync(status == IncomingReplyStatus.Delivered
            ? work.StageFinishAsync(claim, Now, null, token)
            : work.StageHoldAsync(claim, Now, "ReplyDeliveryRequiresReview", token), token);
    }
    private Task ReleaseAsync(InboundClaim claim, DateTimeOffset due, CancellationToken token) =>
        CommitAsync(work.StageFinishAsync(claim, Now, due, token), token);
    private Task HoldAsync(InboundClaim claim, string reason, CancellationToken token) =>
        CommitAsync(work.StageHoldAsync(claim, Now, reason, token), token);

    // Commits a staged receipt disposition together with any staged reply change.
    private async Task CommitAsync(Task<bool> staged, CancellationToken token)
    {
        if (!await staged) throw new PersistenceConcurrencyException("Receipt ownership expired.");
        await unit.SaveAsync(token);
    }
    private async Task<IncomingReplySnapshot> SaveAndReadAsync(InboundClaim claim, CancellationToken token)
    {
        await unit.SaveAsync(token);
        return (await replies.ReadAsync(claim.JournalId, token))!;
    }
    private DateTimeOffset Now => timeProvider.GetUtcNow();
}
