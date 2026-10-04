using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Processing;

/// <summary>Runs one owned initial CBS attempt, or resumes it from committed evidence. Never resubmits a marked payment.</summary>
public sealed class IncomingPacs008Processing(IIncomingProcessingRepository processing, IIncomingPaymentWorkRepository work,
    IUnitOfWork unitOfWork, IIncomingCoreClient core, IIncomingCoreReplyInterpreter replies,
    IncomingProcessingOptions options, TimeProvider timeProvider)
{
    public async Task<IncomingProcessingResult?> ProcessAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var snapshot = await processing.ReadAsync(paymentId, cancellationToken);
        if (snapshot is null) return null;
        var run = new Run(snapshot, new(snapshot.Context.DeadlineUtc, options), unitOfWork, cancellationToken);
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
        if (!run.Payment.HasFinalCoreOutcome)
        {
            if (run.Snapshot.Calls.All(c => c.Kind != CoreCallKind.Submission) && run.Budget.Submission(Now) > TimeSpan.Zero)
            {
                run.Payment.BeginSubmission(Now);
                await CallAsync(run, claim, CoreCallKind.Submission);
            }
            if (run.Payment.CoreStatus is CoreOutcome.SubmissionStarted or CoreOutcome.Unknown && run.Budget.Status(Now) > TimeSpan.Zero)
                await CallAsync(run, claim, CoreCallKind.Status);
        }
        await FinishAsync(run, claim);
    }

    private async Task CallAsync(Run run, IncomingPaymentClaim claim, CoreCallKind kind)
    {
        run.Token.ThrowIfCancellationRequested();
        var call = await processing.StageCallAsync(claim, kind, Now, run.Token);
        await run.CommitAsync(run.Token);
        // The committed marker is evidence even if nothing is sent; dispatch only under live ownership with budget left.
        if (!await processing.IsOwnerAsync(claim, Now, run.Token))
            throw new PersistenceConcurrencyException("Ownership was lost before CBS dispatch.");
        run.Token.ThrowIfCancellationRequested();
        var budget = kind == CoreCallKind.Submission ? run.Budget.Submission(Now) : run.Budget.Status(Now);
        if (budget <= TimeSpan.Zero) return;
        var completion = await DispatchAsync(run, kind, budget);
        // A complete reply survives service cancellation when its original owner is still live.
        using var persistence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
        await processing.StageCompletionAsync(claim, call.Id, completion, Now, persistence.Token);
        await run.CommitAsync(persistence.Token);
        run.Token.ThrowIfCancellationRequested();
        await InterpretAsync(run, claim, call with { Completion = completion });
    }

    // A timed-out or failed call establishes nothing; service shutdown propagates and leaves the marker for recovery.
    private async Task<CoreCallCompletion> DispatchAsync(Run run, CoreCallKind kind, TimeSpan budget)
    {
        using var timeout = new CancellationTokenSource(budget, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(run.Token, timeout.Token);
        try
        {
            var response = await (kind == CoreCallKind.Submission
                ? core.SubmitAsync(run.Payment.ParticipantBic, run.Snapshot.Request, linked.Token)
                : core.QueryAsync(run.Payment.ParticipantBic, run.Payment.EndToEndId, linked.Token)).WaitAsync(linked.Token);
            return new(response, null, Now);
        }
        catch (Exception error) when (!run.Token.IsCancellationRequested)
        {
            return new(null, $"{error.GetType().Name}: {error.Message}", Now);
        }
    }

    private async Task InterpretAsync(Run run, IncomingPaymentClaim claim, IncomingCoreCall call)
    {
        await processing.StageConsumptionAsync(claim, call.Id, Now, run.Token);
        run.Payment.RecordCoreResult(replies.Interpret(call.Completion!, run.Snapshot.Context.Original), Now);
        // Final outcomes, the IPS decision and follow-up release share one commit. Nonfinal evidence is consumed before querying again.
        if (!run.Payment.HasFinalCoreOutcome) await run.CommitAsync(run.Token);
    }

    private async Task FinishAsync(Run run, IncomingPaymentClaim claim)
    {
        run.Token.ThrowIfCancellationRequested();
        if (run.Payment.CoreStatus == CoreOutcome.SubmissionStarted)
            run.Payment.RecordCoreResult(new(CoreOutcome.Unknown, Now), Now);
        run.Payment.DecideIps(run.Budget.WithinReplyWindow(Now), Now);
        await processing.StageFinishAsync(claim, Now, run.Payment.FollowUp == IncomingFollowUp.None ? null : Now + options.FollowUpDelay, run.Token);
        await run.CommitAsync(run.Token);
    }

    private DateTimeOffset Now => timeProvider.GetUtcNow();

    private sealed class Run(IncomingProcessingSnapshot snapshot, ProcessingBudget budget, IUnitOfWork unit, CancellationToken token)
    {
        public IncomingProcessingSnapshot Snapshot { get; } = snapshot;
        public IncomingPayment Payment => Snapshot.Payment;
        public ProcessingBudget Budget { get; } = budget;
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
