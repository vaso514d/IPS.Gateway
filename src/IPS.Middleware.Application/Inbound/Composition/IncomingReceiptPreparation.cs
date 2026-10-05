using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Pacs008;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Application.Inbound.Replies;
using IPS.Middleware.Application.Payments;
using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Inbound.Composition;

public sealed class IncomingReceiptPreparation(
        IInboundReceiptRepository receipts,
        IInboundWorkRepository work,
        IIncomingCompositionRepository composition,
        IncomingPaymentIntake intake,
        IIncomingReplyRepository replies,
        IIncomingReplyProtocol protocol,
        IUnitOfWork unit,
        IncomingCompositionOptions options,
        IncomingReplyOptions replyOptions,
        Pacs008ProtocolProfile profile,
        TimeProvider time)
{
    public async Task<IncomingCompositionResult> PrepareAsync(InboundClaim claim, CancellationToken token)
    {
        var now = time.GetUtcNow();
        if (await work.FindOwnedAsync(claim, now, token) is null)
        {
            return new(IncomingCompositionStatus.OwnershipLost);
        }

        var state = (await composition.ReadAsync(claim.JournalId, token))!;
        if (state.HasReply || state.PaymentId is not null)
        {
            var nextActionAtUtc = state.HasReply ? now : now + options.ContinuationDelay;
            if (!await work.StageFinishAsync(claim, now, nextActionAtUtc, token))
            {
                return new(IncomingCompositionStatus.OwnershipLost);
            }

            await unit.SaveAsync(token);
            return new(state.HasReply ? IncomingCompositionStatus.ReplyReady : IncomingCompositionStatus.Deferred, state.PaymentId);
        }
        var receipt = (await receipts.ReadAsync(claim.JournalId, token))!.Receipt;
        if (receipt.Sequence is not > 0)
        {
            return await HoldAsync("Missing or nonpositive IPS sequence.");
        }

        if (receipt.MessageType is not (PaymentMessageTypes.Pacs008 or "pacs.008.001.12"))
        {
            return await HoldAsync("Unsupported incoming message type.");
        }

        switch (protocol.Read(receipt.RawXml))
        {
            case IncomingPacs008ReadResult.Hold held:
                return await HoldAsync(held.Reason);
            case IncomingPacs008ReadResult.Reject rejected:
                if (!await work.StageOriginalReferencesAsync(claim, rejected.Original, now, token))
                {
                    return new(IncomingCompositionStatus.OwnershipLost);
                }

                var envelope = new IncomingReplyEnvelope(receipt.ParticipantBic, rejected.Original,
                    new(false, now, rejected.ReasonCode, rejected.Description),
                    new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), now), profile, replyOptions.MaxAttempts);
                await replies.StageEnvelopeAsync(claim, envelope, now, token);
                if (!await work.StageFinishAsync(claim, now, now, token))
                {
                    throw new PersistenceConcurrencyException("Receipt ownership expired.");
                }

                await unit.SaveAsync(token);
                return new(IncomingCompositionStatus.ReplyReady);
            case IncomingPacs008ReadResult.Ready ready:
                var registered = await intake.RegisterAndReleaseAsync(claim, ready.Payment, now + options.ContinuationDelay, token);
                return new(registered.Outcome switch
                {
                    IncomingRegistrationOutcome.Conflict => IncomingCompositionStatus.Held,
                    IncomingRegistrationOutcome.LostOwnership => IncomingCompositionStatus.OwnershipLost,
                    _ => IncomingCompositionStatus.Deferred
                }, registered.PaymentId);
            default:
                throw new InvalidOperationException("Unsupported protocol result.");
        }

        async Task<IncomingCompositionResult> HoldAsync(string reason)
        {
            if (!await work.StageHoldAsync(claim, now, reason, token))
            {
                return new(IncomingCompositionStatus.OwnershipLost);
            }

            await unit.SaveAsync(token);
            return new(IncomingCompositionStatus.Held);
        }
    }
}
