using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Processing;
/// <summary>Runs one owned initial CBS attempt, or resumes it from committed evidence. Never resubmits a marked payment.</summary>
public sealed class IncomingPacs008Processing(
        IIncomingProcessingRepository processing,
        IIncomingPaymentWorkRepository work,
        IUnitOfWork unitOfWork,
        IIncomingCoreClient core,
        IIncomingCoreReplyInterpreter replies,
        IncomingProcessingOptions options,
        IncomingReconciliationOptions reconciliationOptions,
        TimeProvider timeProvider)
{
    public async Task<IncomingProcessingResult?> ProcessAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var snapshot = await processing.ReadAsync(paymentId, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        var run = new ProcessingAttempt(snapshot, new(snapshot.Context.DeadlineUtc, options), unitOfWork, cancellationToken);
        if (snapshot.Payment.IpsDecision is not null)
        {
            return run.Committed;
        }

        try
        {
            var claim = await work.StageClaimAsync(paymentId, Now, options.Ownership, cancellationToken);
            if (claim is null)
            {
                return run.Committed;
            }

            await run.CommitAsync(cancellationToken);
            await ContinueAsync(run, claim);
        }
        catch (PersistenceConcurrencyException)
        {
        } // Failed scopes are discarded; no remote effect is replayed.

        return run.Committed;
    }

    private async Task ContinueAsync(ProcessingAttempt run, IncomingPaymentClaim claim)
    {
        foreach (var call in run.Snapshot.Calls.Where(c => c.Completion is not null && !c.Consumed))
        {
            await InterpretAsync(run, claim, call);
        }

        if (!run.Payment.HasFinalCoreOutcome)
        {
            if (run.Snapshot.Calls.All(c => c.Kind != CoreCallKind.Submission) && run.Budget.Submission(Now) > TimeSpan.Zero)
            {
                run.Payment.BeginSubmission(Now);
                await CallAsync(run, claim, CoreCallKind.Submission);
            }

            if (run.Payment.CoreStatus is CoreOutcome.SubmissionStarted or CoreOutcome.Unknown && run.Budget.Status(Now) > TimeSpan.Zero)
            {
                await CallAsync(run, claim, CoreCallKind.Status);
            }
        }

        await FinishAsync(run, claim);
    }

    private async Task CallAsync(ProcessingAttempt run, IncomingPaymentClaim claim, CoreCallKind kind)
    {
        run.Token.ThrowIfCancellationRequested();
        var call = await processing.StageCallAsync(claim, kind, Now, run.Token);
        await run.CommitAsync(run.Token);
        // The committed marker is evidence even if nothing is sent; dispatch only under live ownership with budget left.
        if (!await processing.IsOwnerAsync(claim, Now, run.Token))
        {
            throw new PersistenceConcurrencyException("Ownership was lost before CBS dispatch.");
        }

        run.Token.ThrowIfCancellationRequested();
        var budget = kind == CoreCallKind.Submission ? run.Budget.Submission(Now) : run.Budget.Status(Now);
        if (budget <= TimeSpan.Zero)
        {
            return;
        }

        var completion = await DispatchAsync(run, kind, budget);
        // A complete reply survives service cancellation when its original owner is still live.
        using var persistence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
        await processing.StageCompletionAsync(claim, call.Id, completion, Now, persistence.Token);
        await run.CommitAsync(persistence.Token);
        run.Token.ThrowIfCancellationRequested();
        await InterpretAsync(run, claim, call with { Completion = completion });
    }

    private Task<CoreCallCompletion> DispatchAsync(ProcessingAttempt run, CoreCallKind kind, TimeSpan budget) => CoreCallExecution.ExecuteAsync(token => kind == CoreCallKind.Submission
            ? core.SubmitAsync(run.Payment.ParticipantBic, run.Snapshot.Request, token)
            : core.QueryAsync(run.Payment.ParticipantBic, run.Payment.EndToEndId, token), budget, timeProvider, run.Token);
    private async Task InterpretAsync(ProcessingAttempt run, IncomingPaymentClaim claim, IncomingCoreCall call)
    {
        await processing.StageConsumptionAsync(claim, call.Id, Now, run.Token);
        run.Payment.RecordCoreResult(replies.Interpret(call.Completion!, run.Snapshot.Context.Original), Now);
        // Final outcomes, the IPS decision and follow-up release share one commit. Nonfinal evidence is consumed before querying again.
        if (!run.Payment.HasFinalCoreOutcome)
        {
            await run.CommitAsync(run.Token);
        }
    }

    private async Task FinishAsync(ProcessingAttempt run, IncomingPaymentClaim claim)
    {
        run.Token.ThrowIfCancellationRequested();
        if (run.Payment.CoreStatus == CoreOutcome.SubmissionStarted)
        {
            run.Payment.RecordCoreResult(new(CoreOutcome.Unknown, Now), Now);
        }

        run.Payment.DecideIps(run.Budget.WithinReplyWindow(Now), Now);
        DateTimeOffset? deadline = run.Payment.FollowUp == IncomingFollowUp.None ? null : run.Payment.IpsDecidedAtUtc!.Value + reconciliationOptions.Window;
        DateTimeOffset? due = deadline is null ? null : Now + options.FollowUpDelay;
        if (due > deadline)
        {
            due = deadline;
        }

        await processing.StageFinishAsync(claim, Now, due, deadline, run.Token);
        await run.CommitAsync(run.Token);
    }

    private DateTimeOffset Now => timeProvider.GetUtcNow();

    private sealed class ProcessingAttempt(IncomingProcessingSnapshot snapshot, ProcessingBudget budget, IUnitOfWork unit, CancellationToken token)
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
