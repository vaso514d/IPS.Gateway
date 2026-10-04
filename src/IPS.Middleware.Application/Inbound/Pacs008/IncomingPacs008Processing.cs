using IPS.Middleware.Application.Abstractions.Inbound;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Pacs008;

public sealed record IncomingProcessingResult(CoreOutcome CoreStatus, IncomingIpsDecision? IpsDecision, IncomingFollowUp FollowUp);

/// <summary>Runs one owned initial CBS attempt, or resumes it from committed evidence. Never resubmits a marked payment.</summary>
public sealed class IncomingPacs008Processing(IIncomingProcessingRepository processing, IIncomingPaymentWorkRepository work,
    IUnitOfWork unitOfWork, IIncomingCoreClient core, IIncomingCoreReplyInterpreter replies,
    IncomingProcessingOptions options, TimeProvider timeProvider)
{
    public async Task<IncomingProcessingResult?> ProcessAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var snapshot = await processing.ReadAsync(paymentId, cancellationToken);
        if (snapshot is null) return null;
        var run = new Run(snapshot, unitOfWork, cancellationToken);
        if (snapshot.Payment.IpsDecision is not null) return run.Committed;
        try
        {
            var claim = await work.StageClaimAsync(paymentId, Now, options.Ownership, cancellationToken);
            if (claim is null) return run.Committed;
            await run.CommitAsync(cancellationToken);
            await ContinueAsync(run, claim);
        }
        catch (PersistenceConcurrencyException) { } // Failed scopes are discarded; no remote effect is replayed.
        return run.Committed;
    }

    private async Task ContinueAsync(Run run, IncomingPaymentClaim claim)
    {
        foreach (var call in run.Snapshot.Calls.Where(c => c.Completion is not null && !c.Consumed))
            await InterpretAsync(run, claim, call);
        if (run.Payment.CoreStatus is CoreOutcome.Accepted or CoreOutcome.Rejected)
        {
            await FinishAsync(run, claim);
            return;
        }

        if (run.Snapshot.Calls.All(c => c.Kind != CoreCallKind.Submission) && Budget(run, CoreCallKind.Submission) > TimeSpan.Zero)
        {
            run.Payment.BeginSubmission(Now);
            await CallAsync(run, claim, CoreCallKind.Submission);
        }
        if (run.Payment.CoreStatus is CoreOutcome.SubmissionStarted or CoreOutcome.Unknown && Budget(run, CoreCallKind.Status) > TimeSpan.Zero)
            await CallAsync(run, claim, CoreCallKind.Status);
        await FinishAsync(run, claim);
    }

    private async Task CallAsync(Run run, IncomingPaymentClaim claim, CoreCallKind kind)
    {
        run.Token.ThrowIfCancellationRequested();
        var call = await processing.StageCallAsync(claim, kind, Now, run.Token);
        await run.CommitAsync(run.Token);
        var budget = Budget(run, kind);
        if (budget <= TimeSpan.Zero) return;
        if (!await processing.IsOwnerAsync(claim, Now, run.Token))
            throw new PersistenceConcurrencyException("Ownership was lost before CBS dispatch.");
        run.Token.ThrowIfCancellationRequested();
        budget = Budget(run, kind);
        if (budget <= TimeSpan.Zero) return;
        using var timeout = new CancellationTokenSource(budget, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(run.Token, timeout.Token);
        CoreCallCompletion completion;
        try
        {
            var response = await (kind == CoreCallKind.Submission
                ? core.SubmitAsync(run.Payment.ParticipantBic, run.Snapshot.Request, linked.Token)
                : core.QueryAsync(run.Payment.ParticipantBic, run.Payment.EndToEndId, linked.Token)).WaitAsync(linked.Token);
            completion = new(response, null, Now);
        }
        catch (Exception error) when (!run.Token.IsCancellationRequested)
        {
            completion = new(null, $"{error.GetType().Name}: {error.Message}", Now);
        }
        // A complete reply survives service cancellation when its original owner is still live.
        using var persistence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
        await processing.StageCompletionAsync(claim, call.Id, completion, Now, persistence.Token);
        await run.CommitAsync(persistence.Token);
        run.Token.ThrowIfCancellationRequested();
        await InterpretAsync(run, claim, call with { Completion = completion });
    }

    private async Task InterpretAsync(Run run, IncomingPaymentClaim claim, IncomingCoreCall call)
    {
        await processing.StageConsumptionAsync(claim, call.Id, Now, run.Token);
        run.Payment.RecordCoreResult(replies.Interpret(call.Completion!, run.Snapshot.Context.Original), Now);
        // Final outcomes, the IPS decision and follow-up release share one commit. Nonfinal evidence is consumed before querying again.
        if (run.Payment.CoreStatus is not (CoreOutcome.Accepted or CoreOutcome.Rejected)) await run.CommitAsync(run.Token);
    }

    private async Task FinishAsync(Run run, IncomingPaymentClaim claim)
    {
        run.Token.ThrowIfCancellationRequested();
        if (run.Payment.CoreStatus == CoreOutcome.SubmissionStarted)
            run.Payment.RecordCoreResult(new(CoreOutcome.Unknown, Now), Now);
        run.Payment.DecideIps(Now < run.Snapshot.Context.DeadlineUtc - options.ReplyReserve, Now);
        await processing.StageFinishAsync(claim, Now, run.Payment.FollowUp == IncomingFollowUp.None ? null : Now + options.FollowUpDelay, run.Token);
        await run.CommitAsync(run.Token);
    }

    private TimeSpan Budget(Run run, CoreCallKind kind)
    {
        var remaining = run.Snapshot.Context.DeadlineUtc - Now - options.ReplyReserve;
        var budget = kind == CoreCallKind.Submission ? remaining - options.StatusBudget : TimeSpan.FromTicks(Math.Min(remaining.Ticks, options.StatusBudget.Ticks));
        return budget > TimeSpan.Zero ? budget : TimeSpan.Zero;
    }
    private DateTimeOffset Now => timeProvider.GetUtcNow();

    private sealed class Run(IncomingProcessingSnapshot snapshot, IUnitOfWork unit, CancellationToken token)
    {
        public IncomingProcessingSnapshot Snapshot { get; } = snapshot;
        public IncomingPayment Payment => Snapshot.Payment;
        public CancellationToken Token { get; } = token;
        public IncomingProcessingResult Committed { get; private set; } = State(snapshot.Payment);
        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await unit.SaveAsync(cancellationToken);
            Committed = State(Payment);
        }
        private static IncomingProcessingResult State(IncomingPayment payment) => new(payment.CoreStatus, payment.IpsDecision, payment.FollowUp);
    }
}
