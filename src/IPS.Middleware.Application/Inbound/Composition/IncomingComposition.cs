using IPS.Middleware.Application.Inbound.Receipts;

namespace IPS.Middleware.Application.Inbound.Composition;

// Drives one receipt through registration, payment processing and its reply, each step in its own owned scope.
public sealed class IncomingComposition(IIncomingWorkflowExecution execution, TimeProvider timeProvider)
{
    public async Task<IncomingCompositionResult> ProcessAsync(Guid journalId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var state = await execution.ReadAsync(journalId, token);
        if (state is null || state.Status == InboundProcessingStatus.Processed)
        {
            return new(IncomingCompositionStatus.Terminal);
        }

        if (state.Status == InboundProcessingStatus.Held)
        {
            return new(IncomingCompositionStatus.Held, state.PaymentId);
        }

        if (state.HasReply)
        {
            return await DeliverAsync(journalId, token);
        }

        if (!state.HasDecision && state.NextActionAtUtc > timeProvider.GetUtcNow())
        {
            return new(IncomingCompositionStatus.Deferred, state.PaymentId);
        }

        var paymentId = state.PaymentId;
        if (!state.HasDecision)
        {
            var prepared = await execution.PrepareAsync(journalId, token);
            if (prepared.Status == IncomingCompositionStatus.ReplyReady)
            {
                return await DeliverAsync(journalId, token);
            }

            if (prepared.Status != IncomingCompositionStatus.Deferred || prepared.PaymentId is null)
            {
                return prepared;
            }

            paymentId = prepared.PaymentId;
        }

        token.ThrowIfCancellationRequested();
        var attachedPaymentId = paymentId ?? throw new InvalidOperationException("A payment decision requires an attached payment.");
        var payment = await execution.ProcessPaymentAsync(attachedPaymentId, token);
        if (payment?.IpsDecision is null)
        {
            return new(IncomingCompositionStatus.Deferred, paymentId);
        }

        if (!await execution.MakeFirstReplyReadyAsync(journalId, token))
        {
            return new(IncomingCompositionStatus.OwnershipLost, paymentId);
        }

        return await DeliverAsync(journalId, token);
    }

    private async Task<IncomingCompositionResult> DeliverAsync(Guid journalId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await execution.DeliverReplyAsync(journalId, token);
        // A fresh read sees only committed scheduling, including a competing owner's result.
        var state = await execution.ReadAsync(journalId, token);
        if (state is null || state.Status == InboundProcessingStatus.Processed)
        {
            return new(IncomingCompositionStatus.Terminal, state?.PaymentId);
        }

        if (state.Status == InboundProcessingStatus.Held)
        {
            return new(IncomingCompositionStatus.Held, state.PaymentId);
        }

        if (state.HasReply && state.NextActionAtUtc is not null)
        {
            execution.TryNotifyReply(journalId);
            return new(IncomingCompositionStatus.ReplyReady, state.PaymentId);
        }

        return new(IncomingCompositionStatus.Deferred, state.PaymentId);
    }
}
