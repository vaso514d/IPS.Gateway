using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Inbound.Replies;

// Prepares a receipt's immutable reply, then performs one durable send attempt per invocation.
public sealed class IncomingReplyProcessing(
    IInboundReceiptRepository receipts,
    IInboundWorkRepository work,
    IIncomingReplyRepository replies,
    IUnitOfWork unitOfWork,
    IIncomingReplyProtocol protocol,
    IIncomingReplyClient client,
    Pacs008ProtocolProfile profile,
    IncomingReplyOptions options,
    TimeProvider timeProvider)
{
    private const string AttemptsExhausted = "Reply attempt budget exhausted without a conclusive IPS result.";
    private const string ReviewHoldReason = "ReplyDeliveryRequiresReview";

    private DateTimeOffset Now => timeProvider.GetUtcNow();

    public async Task ProcessAsync(Guid journalId, CancellationToken cancellationToken)
    {
        try
        {
            var claim = await work.StageClaimAsync(journalId, Now, options.Ownership, cancellationToken);
            if (claim is null)
            {
                return;
            }

            await unitOfWork.SaveAsync(cancellationToken);
            var reply = await replies.ReadAsync(journalId, cancellationToken) ?? await PrepareEnvelopeAsync(claim, cancellationToken);
            if (reply is null)
            {
                return;
            }

            if (reply.Status is IncomingReplyStatus.Delivered or IncomingReplyStatus.ManualReview)
            {
                throw new InvalidOperationException("A terminal reply must not have a pending receipt.");
            }

            reply = await PrepareMessageAsync(claim, reply, cancellationToken);
            if (reply is not null)
            {
                await DeliverAsync(claim, reply, cancellationToken);
            }
        }
        catch (PersistenceConcurrencyException)
        {
            // Dispose this scope; a newer owner or receipt update won.
        }
    }

    // Freezes the reply context once; null when the receipt is held or its payment decision is not yet committed.
    private async Task<IncomingReplySnapshot?> PrepareEnvelopeAsync(InboundClaim claim, CancellationToken token)
    {
        var receipt = (await receipts.ReadAsync(claim.JournalId, token))!.Receipt;
        if (!PaymentMessageTypes.IsPacs008(receipt.MessageType))
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
                decision = new IncomingReplyDecision(false, Now, rejected.ReasonCode, rejected.Description);
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
            default:
                throw new InvalidOperationException("Unsupported protocol result.");
        }

        await work.StageOriginalReferencesAsync(claim, original, Now, token);
        var envelope = new IncomingReplyEnvelope(receipt.ParticipantBic, original, decision, IncomingReplyContext.New(Now), profile, options.MaxAttempts);
        await replies.StageEnvelopeAsync(claim, envelope, Now, token);
        return await SaveAndReadAsync(claim, token);
    }

    private async Task<IncomingReplySnapshot?> PrepareMessageAsync(InboundClaim claim, IncomingReplySnapshot reply, CancellationToken token)
    {
        if (reply.UnsignedXml is null)
        {
            await replies.StageUnsignedAsync(claim, protocol.Build(reply.Envelope), Now, token);
            reply = await SaveAndReadAsync(claim, token);
        }

        if (reply.MessageXml is null)
        {
            if (await protocol.SignAsync(reply.UnsignedXml!, token) is not SignedMessage signed)
            {
                await ReleaseAsync(claim, Now + options.PreparationRetryDelay, token);
                return null;
            }

            await replies.StageMessageAsync(claim, signed, Now, token);
            reply = await SaveAndReadAsync(claim, token);
        }

        return reply;
    }

    private async Task DeliverAsync(InboundClaim claim, IncomingReplySnapshot reply, CancellationToken token)
    {
        foreach (var saved in reply.Attempts.Where(attempt => attempt.Completion is not null && !attempt.Consumed))
        {
            if (await InterpretAsync(claim, reply.Envelope, saved, token))
            {
                return;
            }
        }

        if (reply.Attempts.Count >= reply.Envelope.MaxAttempts)
        {
            await FinishAsync(claim, IncomingReplyStatus.ManualReview, AttemptsExhausted, token);
            return;
        }

        if (NextAttemptAt(reply) is { } due && due > Now)
        {
            await ReleaseAsync(claim, due, token);
            return;
        }

        await SendAttemptAsync(claim, reply, token);
    }

    private async Task SendAttemptAsync(InboundClaim claim, IncomingReplySnapshot reply, CancellationToken token)
    {
        var attempt = await replies.StageAttemptAsync(claim, Now, token);
        await unitOfWork.SaveAsync(token);
        if (!await replies.IsOwnerAsync(claim, Now, token))
        {
            return;
        }

        token.ThrowIfCancellationRequested();
        // No database transaction spans the send. Clamp to remaining ownership, not the original payment deadline.
        var ownershipLeft = claim.ExpiresAtUtc - Now - options.PersistenceBudget;
        var callBudget = ownershipLeft < options.CallTimeout ? ownershipLeft : options.CallTimeout;
        if (callBudget <= TimeSpan.Zero)
        {
            return;
        }

        var completion = await SendAsync(reply, callBudget, token);
        using var persistence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
        await replies.StageCompletionAsync(claim, attempt.Id, completion, Now, persistence.Token);
        await unitOfWork.SaveAsync(persistence.Token);
        token.ThrowIfCancellationRequested();
        if (await InterpretAsync(claim, reply.Envelope, attempt with { Completion = completion }, token))
        {
            return;
        }

        if (attempt.Number >= reply.Envelope.MaxAttempts)
        {
            await FinishAsync(claim, IncomingReplyStatus.ManualReview, AttemptsExhausted, token);
        }
        else
        {
            await ReleaseAsync(claim, Now + options.RetryDelay, token);
        }
    }

    private async Task<ReplyAttemptCompletion> SendAsync(IncomingReplySnapshot reply, TimeSpan budget, CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(budget, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        try
        {
            var response = await client.SendAsync(reply.Envelope.ParticipantBic, reply.MessageXml!, linked.Token).WaitAsync(linked.Token);
            return new ReplyAttemptCompletion(response, null, Now);
        }
        catch (Exception error)
        {
            return new ReplyAttemptCompletion(null, $"{error.GetType().Name}: {error.Message}", Now);
        }
    }

    // Consumes saved evidence; true when it settled the reply and the receipt.
    private async Task<bool> InterpretAsync(InboundClaim claim, IncomingReplyEnvelope envelope, IncomingReplyAttempt attempt, CancellationToken token)
    {
        var result = protocol.Interpret(attempt.Completion!, envelope);
        await replies.StageConsumptionAsync(claim, attempt.Id, Now, token);
        switch (result.Outcome)
        {
            case ReplyDeliveryOutcome.Delivered:
                await FinishAsync(claim, IncomingReplyStatus.Delivered, null, token);
                return true;
            case ReplyDeliveryOutcome.Conflict:
                await FinishAsync(claim, IncomingReplyStatus.ManualReview, result.Description, token);
                return true;
            default:
                await unitOfWork.SaveAsync(token);
                return false;
        }
    }

    // Recovery respects the durable attempt time; a restart cannot skip the retry delay.
    private DateTimeOffset? NextAttemptAt(IncomingReplySnapshot reply)
    {
        if (reply.Attempts.LastOrDefault() is not { } last)
        {
            return null;
        }

        var lastActivity = last.Completion?.ObservedAtUtc ?? last.StartedAtUtc;
        return lastActivity + options.RetryDelay;
    }

    private async Task FinishAsync(InboundClaim claim, IncomingReplyStatus status, string? reason, CancellationToken token)
    {
        await replies.StageOutcomeAsync(claim, status, reason, Now, token);
        var staged = status == IncomingReplyStatus.Delivered
            ? await work.StageFinishAsync(claim, Now, null, token)
            : await work.StageHoldAsync(claim, Now, ReviewHoldReason, token);
        await CommitReceiptAsync(staged, token);
    }

    private async Task ReleaseAsync(InboundClaim claim, DateTimeOffset due, CancellationToken token) =>
        await CommitReceiptAsync(await work.StageFinishAsync(claim, Now, due, token), token);

    private async Task HoldAsync(InboundClaim claim, string reason, CancellationToken token) =>
        await CommitReceiptAsync(await work.StageHoldAsync(claim, Now, reason, token), token);

    // Commits the receipt's new schedule together with any staged reply change.
    private async Task CommitReceiptAsync(bool staged, CancellationToken token)
    {
        if (!staged)
        {
            throw new PersistenceConcurrencyException("Receipt ownership expired.");
        }

        await unitOfWork.SaveAsync(token);
    }

    private async Task<IncomingReplySnapshot> SaveAndReadAsync(InboundClaim claim, CancellationToken token)
    {
        await unitOfWork.SaveAsync(token);
        return (await replies.ReadAsync(claim.JournalId, token))!;
    }
}
