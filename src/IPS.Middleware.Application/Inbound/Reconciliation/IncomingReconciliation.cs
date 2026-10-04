using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Reconciliation;

public sealed class IncomingReconciliation(IIncomingReconciliationRepository work, IIncomingProcessingRepository processing,
    IUnitOfWork unitOfWork, IIncomingCoreClient core, IIncomingReversalClient reversals, IIncomingCoreReplyInterpreter replies,
    IncomingReconciliationOptions options, TimeProvider timeProvider)
{
    public Task<IReadOnlyList<Guid>> DiscoverAsync(CancellationToken token) => work.FindDueAsync(Now, options.DiscoveryBatch, token);

    public async Task<IncomingProcessingResult?> ProcessAsync(Guid paymentId, CancellationToken token)
    {
        var snapshot = await processing.ReadAsync(paymentId, token);
        if (snapshot is null) return null;
        var committed = State(snapshot.Payment);
        try
        {
            var claim = await work.StageClaimAsync(paymentId, Now, options.Ownership, token);
            if (claim is null) return committed;
            await unitOfWork.SaveAsync(token);
            var payment = snapshot.Payment;
            var deadline = snapshot.ReconciliationDeadlineUtc ?? throw new InvalidOperationException("Follow-up work requires a frozen reconciliation deadline.");
            var attempts = snapshot.Calls.Count(c => c.Kind == CoreCallKind.Reconciliation);
            var replayed = false;
            foreach (var call in snapshot.Calls.Where(c => c.Kind is CoreCallKind.Reconciliation or CoreCallKind.Reversal && c.Completion is not null && !c.Consumed))
            {
                await InterpretAsync(payment, snapshot, claim, call, token);
                replayed = true;
            }

            // A reversal marker may mean the request already changed CBS, even when no reply was stored.
            if (payment.Reversal == ReversalDelivery.Started)
                payment.RecordReversalDelivery(ReversalDelivery.Uncertain, Now);
            if (payment.FollowUp is IncomingFollowUp.None or IncomingFollowUp.ManualReviewRequired)
                return await FinishAsync(payment, claim, null, token);
            if (Now >= deadline)
            {
                payment.RequireManualReview("The fixed CBS reconciliation window expired.", Now);
                return await FinishAsync(payment, claim, null, token);
            }
            // A replayed result must commit with its resulting schedule before any further remote effect.
            if (!replayed)
            {
                var kind = payment.FollowUp == IncomingFollowUp.ReversalRequired ? CoreCallKind.Reversal : CoreCallKind.Reconciliation;
                ReversalNotification? notification = null;
                if (kind == CoreCallKind.Reversal)
                {
                    var decision = payment.IpsDecision!;
                    notification = new(payment.Id, payment.ParticipantBic, payment.EndToEndId, decision.DecidedAtUtc,
                        decision.ReasonCode, decision.Description, snapshot.Context.Original.GroupMessageId);
                    payment.BeginReversal(Now);
                }
                var call = await processing.StageCallAsync(claim, kind, Now, token, notification);
                await unitOfWork.SaveAsync(token);
                committed = State(payment);
                if (kind == CoreCallKind.Reconciliation) attempts++;
                if (!await processing.IsOwnerAsync(claim, Now, token)) throw new PersistenceConcurrencyException("Reconciliation owner was lost before dispatch.");
                token.ThrowIfCancellationRequested();
                var budget = TimeSpan.FromTicks(Math.Min(options.CallTimeout.Ticks, (deadline - Now).Ticks));
                if (budget > TimeSpan.Zero)
                {
                    var completion = await DispatchAsync(snapshot, call, budget, token);
                    using var persistence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
                    await processing.StageCompletionAsync(claim, call.Id, completion, Now, persistence.Token);
                    await unitOfWork.SaveAsync(persistence.Token);
                    token.ThrowIfCancellationRequested();
                    await InterpretAsync(payment, snapshot, claim, call with { Completion = completion }, token);
                }
                else if (kind == CoreCallKind.Reversal)
                    payment.RecordReversalDelivery(ReversalDelivery.Uncertain, Now);
            }
            if (payment.FollowUp is IncomingFollowUp.None or IncomingFollowUp.ManualReviewRequired)
                return await FinishAsync(payment, claim, null, token);
            if (Now >= deadline)
            {
                payment.RequireManualReview("The fixed CBS reconciliation window expired.", Now);
                return await FinishAsync(payment, claim, null, token);
            }
            var next = payment.FollowUp == IncomingFollowUp.ReversalRequired ? Now : Now + options.RetryDelay(attempts);
            return await FinishAsync(payment, claim, next < deadline ? next : deadline, token);
        }
        catch (PersistenceConcurrencyException) { return committed; } // Discard this scope; never replay remote effects on a failed unit of work.
    }

    private async Task InterpretAsync(IncomingPayment payment, IncomingProcessingSnapshot snapshot, IncomingPaymentClaim claim,
        IncomingCoreCall call, CancellationToken token)
    {
        await processing.StageConsumptionAsync(claim, call.Id, Now, token);
        if (call.Kind == CoreCallKind.Reversal)
            payment.RecordReversalDelivery(call.Completion!.Response is { } response
                ? response.StatusCode is >= 200 and < 300 ? ReversalDelivery.Accepted : ReversalDelivery.Unsuccessful
                : ReversalDelivery.Uncertain, call.Completion!.ObservedAtUtc);
        else
            payment.RecordCoreResult(replies.Interpret(call.Completion!, snapshot.Context.Original), Now);
    }

    private Task<CoreCallCompletion> DispatchAsync(IncomingProcessingSnapshot snapshot, IncomingCoreCall call, TimeSpan budget, CancellationToken token) =>
        CoreCallExecution.ExecuteAsync(callToken => call.Kind == CoreCallKind.Reversal
            ? reversals.RequestAsync(call.Notification!, callToken)
            : core.QueryAsync(snapshot.Payment.ParticipantBic, snapshot.Payment.EndToEndId, callToken), budget, timeProvider, token);

    private async Task<IncomingProcessingResult> FinishAsync(IncomingPayment payment, IncomingPaymentClaim claim, DateTimeOffset? next, CancellationToken token)
    {
        await work.StageReleaseAsync(claim, Now, next, token);
        await unitOfWork.SaveAsync(token);
        return State(payment);
    }
    private static IncomingProcessingResult State(IncomingPayment payment) => new(payment.CoreStatus, payment.IpsDecision, payment.FollowUp);
    private DateTimeOffset Now => timeProvider.GetUtcNow();
}
