using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Reconciliation;

public sealed class IncomingReconciliation(
        IIncomingReconciliationRepository work,
        IIncomingProcessingRepository processing,
        IUnitOfWork unitOfWork,
        IIncomingCoreClient core,
        IIncomingReversalClient reversals,
        IIncomingCoreReplyInterpreter replies,
        IncomingReconciliationOptions options,
        TimeProvider timeProvider)
{
    public Task<IReadOnlyList<Guid>> DiscoverAsync(CancellationToken token) => work.FindDueAsync(Now, options.DiscoveryBatch, token);
    public async Task<IncomingProcessingResult?> ProcessAsync(Guid paymentId, CancellationToken token)
    {
        var snapshot = await processing.ReadAsync(paymentId, token);
        if (snapshot is null)
        {
            return null;
        }

        var run = new ReconciliationRun(snapshot, unitOfWork);
        try
        {
            var claim = await work.StageClaimAsync(paymentId, Now, options.Ownership, token);
            if (claim is null)
            {
                return run.Committed;
            }

            await run.CommitAsync(token);
            await ContinueAsync(run, claim, token);
        }
        catch (PersistenceConcurrencyException)
        {
            // Discard this scope; never replay remote effects on a failed unit of work.
        }

        return run.Committed;
    }

    private async Task ContinueAsync(ReconciliationRun run, IncomingPaymentClaim claim, CancellationToken token)
    {
        var deadline = run.Snapshot.ReconciliationDeadlineUtc
            ?? throw new InvalidOperationException("Follow-up work requires a frozen reconciliation deadline.");
        var replayed = false;
        foreach (var call in run.Snapshot.Calls.Where(c =>
            c.Kind is CoreCallKind.Reconciliation or CoreCallKind.Reversal && c.Completion is not null && !c.Consumed))
        {
            await InterpretAsync(run.Payment, run.Snapshot, claim, call, token);
            replayed = true;
        }

        // A reversal marker may mean the request already changed CBS, even when no reply was stored.
        if (run.Payment.Reversal == ReversalDelivery.Started)
        {
            run.Payment.RecordReversalDelivery(ReversalDelivery.Uncertain, Now);
        }

        if (await FinishIfClosedAsync(run, claim, deadline, token))
        {
            return;
        }

        // Replayed results and their resulting schedule commit before any further remote effect.
        if (!replayed)
        {
            await CallAsync(run, claim, deadline, token);
        }

        if (await FinishIfClosedAsync(run, claim, deadline, token))
        {
            return;
        }

        var next = run.Payment.FollowUp == IncomingFollowUp.ReversalRequired ? Now : Now + options.RetryDelay(run.Attempts);
        await FinishAsync(run, claim, next < deadline ? next : deadline, token);
    }

    private async Task CallAsync(ReconciliationRun run, IncomingPaymentClaim claim, DateTimeOffset deadline, CancellationToken token)
    {
        var kind = run.Payment.FollowUp == IncomingFollowUp.ReversalRequired ? CoreCallKind.Reversal : CoreCallKind.Reconciliation;
        var notification = kind == CoreCallKind.Reversal ? BeginReversal(run) : null;
        var call = await processing.StageCallAsync(claim, kind, Now, token, notification);
        await run.CommitAsync(token);
        if (kind == CoreCallKind.Reconciliation)
        {
            run.Attempts++;
        }

        if (!await processing.IsOwnerAsync(claim, Now, token))
        {
            throw new PersistenceConcurrencyException("Reconciliation owner was lost before dispatch.");
        }

        token.ThrowIfCancellationRequested();
        var budget = TimeSpan.FromTicks(Math.Min(options.CallTimeout.Ticks, (deadline - Now).Ticks));
        if (budget > TimeSpan.Zero)
        {
            var completion = await DispatchAsync(run.Snapshot, call, budget, token);
            using var persistence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
            await processing.StageCompletionAsync(claim, call.Id, completion, Now, persistence.Token);
            await run.CommitAsync(persistence.Token);
            token.ThrowIfCancellationRequested();
            await InterpretAsync(run.Payment, run.Snapshot, claim, call with { Completion = completion }, token);
        }
        else if (kind == CoreCallKind.Reversal)
        {
            run.Payment.RecordReversalDelivery(ReversalDelivery.Uncertain, Now);
        }
    }

    private ReversalNotification BeginReversal(ReconciliationRun run)
    {
        var payment = run.Payment;
        var decision = payment.IpsDecision!;
        var notification = new ReversalNotification(payment.Id, payment.ParticipantBic, payment.EndToEndId, decision.DecidedAtUtc,
            decision.ReasonCode, decision.Description, run.Snapshot.Context.Original.GroupMessageId);
        payment.BeginReversal(Now);
        return notification;
    }

    private async Task<bool> FinishIfClosedAsync(
        ReconciliationRun run, IncomingPaymentClaim claim, DateTimeOffset deadline, CancellationToken token)
    {
        if (run.Payment.FollowUp is IncomingFollowUp.None or IncomingFollowUp.ManualReviewRequired)
        {
            await FinishAsync(run, claim, null, token);
            return true;
        }

        if (Now >= deadline)
        {
            run.Payment.RequireManualReview("The fixed CBS reconciliation window expired.", Now);
            await FinishAsync(run, claim, null, token);
            return true;
        }

        return false;
    }

    private async Task InterpretAsync(
        IncomingPayment payment,
        IncomingProcessingSnapshot snapshot,
        IncomingPaymentClaim claim,
        IncomingCoreCall call,
        CancellationToken token)
    {
        await processing.StageConsumptionAsync(claim, call.Id, Now, token);
        if (call.Kind == CoreCallKind.Reversal)
        {
            payment.RecordReversalDelivery(ReversalOutcome(call.Completion!), call.Completion!.ObservedAtUtc);
        }
        else
        {
            payment.RecordCoreResult(replies.Interpret(call.Completion!, snapshot.Context.Original), Now);
        }
    }

    private Task<CoreCallCompletion> DispatchAsync(IncomingProcessingSnapshot snapshot, IncomingCoreCall call, TimeSpan budget, CancellationToken token) => CoreCallExecution.ExecuteAsync(callToken => call.Kind == CoreCallKind.Reversal
            ? reversals.RequestAsync(call.Notification!, callToken)
            : core.QueryAsync(snapshot.Payment.ParticipantBic, snapshot.Payment.EndToEndId, callToken), budget, timeProvider, token);
    private async Task FinishAsync(ReconciliationRun run, IncomingPaymentClaim claim, DateTimeOffset? next, CancellationToken token)
    {
        await work.StageReleaseAsync(claim, Now, next, token);
        await run.CommitAsync(token);
    }

    private static ReversalDelivery ReversalOutcome(CoreCallCompletion completion)
    {
        if (completion.Response is null)
        {
            return ReversalDelivery.Uncertain;
        }

        return completion.Response.StatusCode is >= 200 and < 300 ? ReversalDelivery.Accepted : ReversalDelivery.Unsuccessful;
    }

    private static IncomingProcessingResult State(IncomingPayment payment) => new(payment.CoreStatus, payment.IpsDecision, payment.FollowUp);
    private DateTimeOffset Now => timeProvider.GetUtcNow();

    private sealed class ReconciliationRun(IncomingProcessingSnapshot snapshot, IUnitOfWork unit)
    {
        public IncomingProcessingSnapshot Snapshot { get; } = snapshot;
        public IncomingPayment Payment => Snapshot.Payment;
        public int Attempts { get; set; } = snapshot.Calls.Count(c => c.Kind == CoreCallKind.Reconciliation);
        public IncomingProcessingResult Committed { get; private set; } = State(snapshot.Payment);

        public async Task CommitAsync(CancellationToken token)
        {
            await unit.SaveAsync(token);
            Committed = State(Payment);
        }
    }
}
