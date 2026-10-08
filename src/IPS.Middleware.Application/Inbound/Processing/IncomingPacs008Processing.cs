using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Application.Inbound.Registration;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Processing;

// Runs one owned initial CBS attempt, or resumes it from committed evidence. Never resubmits a marked payment.
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
    private DateTimeOffset Now => timeProvider.GetUtcNow();

    public async Task<IncomingProcessingResult?> ProcessAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var snapshot = await processing.ReadAsync(paymentId, cancellationToken);
        if (snapshot is null)
        {
            return null;
        }

        var current = IncomingProcessingResult.Of(snapshot.Payment);
        if (snapshot.Payment.IpsDecision is not null)
        {
            return current;
        }

        ClaimedIncomingPayment? claimed = null;
        try
        {
            var claim = await work.StageClaimAsync(paymentId, Now, options.Ownership, cancellationToken);
            if (claim is null)
            {
                return current;
            }

            claimed = new ClaimedIncomingPayment(snapshot, claim, unitOfWork);
            await claimed.CommitAsync(cancellationToken);
            await ContinueAsync(claimed, cancellationToken);
        }
        catch (PersistenceConcurrencyException)
        {
            // Failed scopes are discarded; no remote effect is replayed.
        }

        return claimed?.Committed ?? current;
    }

    private async Task ContinueAsync(ClaimedIncomingPayment claimed, CancellationToken cancellationToken)
    {
        var payment = claimed.Payment;
        var budget = new ProcessingBudget(claimed.Snapshot.Context.DeadlineUtc, options);

        foreach (var call in claimed.Snapshot.Calls.Where(call => call.Completion is not null && !call.Consumed))
        {
            await InterpretAsync(claimed, call, cancellationToken);
        }

        if (!payment.HasFinalCoreOutcome)
        {
            var submitted = claimed.Snapshot.Calls.Any(call => call.Kind == CoreCallKind.Submission);
            if (!submitted && budget.Submission(Now) > TimeSpan.Zero)
            {
                payment.BeginSubmission(Now);
                await CallAsync(claimed, CoreCallKind.Submission, budget, cancellationToken);
            }

            if (payment.CoreStatus is CoreOutcome.SubmissionStarted or CoreOutcome.Unknown && budget.Status(Now) > TimeSpan.Zero)
            {
                await CallAsync(claimed, CoreCallKind.Status, budget, cancellationToken);
            }
        }

        await FinishAsync(claimed, budget, cancellationToken);
    }

    // The committed marker is evidence even if nothing is sent; dispatch only under live ownership with budget left.
    private async Task CallAsync(ClaimedIncomingPayment claimed, CoreCallKind kind, ProcessingBudget budget, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var call = await processing.StageCallAsync(claimed.Claim, kind, Now, cancellationToken);
        await claimed.CommitAsync(cancellationToken);
        if (!await processing.IsOwnerAsync(claimed.Claim, Now, cancellationToken))
        {
            throw new PersistenceConcurrencyException("Ownership was lost before CBS dispatch.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var callBudget = kind == CoreCallKind.Submission ? budget.Submission(Now) : budget.Status(Now);
        if (callBudget <= TimeSpan.Zero)
        {
            return;
        }

        var completion = await CoreCallExecution.ExecuteAsync(
            callToken => SendAsync(claimed.Snapshot, kind, callToken),
            callBudget,
            timeProvider,
            cancellationToken);

        // A complete reply survives service cancellation when its original owner is still live.
        using var persistence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
        await processing.StageCompletionAsync(claimed.Claim, call.Id, completion, Now, persistence.Token);
        await claimed.CommitAsync(persistence.Token);
        cancellationToken.ThrowIfCancellationRequested();
        await InterpretAsync(claimed, call with { Completion = completion }, cancellationToken);
    }

    private Task<CoreResponse> SendAsync(IncomingProcessingSnapshot snapshot, CoreCallKind kind, CancellationToken callToken) =>
        kind == CoreCallKind.Submission
            ? core.SubmitAsync(snapshot.Payment.ParticipantBic, snapshot.Request, callToken)
            : core.QueryAsync(snapshot.Payment.ParticipantBic, snapshot.Payment.EndToEndId, callToken);

    private async Task InterpretAsync(ClaimedIncomingPayment claimed, IncomingCoreCall call, CancellationToken cancellationToken)
    {
        await processing.StageConsumptionAsync(claimed.Claim, call.Id, Now, cancellationToken);
        var result = replies.Interpret(call.Completion!, claimed.Snapshot.Context.Original);
        claimed.Payment.RecordCoreResult(result, Now);

        // Final outcomes, the IPS decision and follow-up release share one commit. Nonfinal evidence is consumed before querying again.
        if (!claimed.Payment.HasFinalCoreOutcome)
        {
            await claimed.CommitAsync(cancellationToken);
        }
    }

    private async Task FinishAsync(ClaimedIncomingPayment claimed, ProcessingBudget budget, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payment = claimed.Payment;
        if (payment.CoreStatus == CoreOutcome.SubmissionStarted)
        {
            payment.RecordCoreResult(new CorePaymentResult(CoreOutcome.Unknown, Now), Now);
        }

        payment.DecideIps(budget.WithinReplyWindow(Now), Now);
        var deadline = ReconciliationDeadline(payment);
        var followUpAt = deadline is null ? (DateTimeOffset?)null : Earliest(Now + options.FollowUpDelay, deadline.Value);

        await processing.StageFinishAsync(claimed.Claim, Now, followUpAt, deadline, cancellationToken);
        await claimed.CommitAsync(cancellationToken);
    }

    // A follow-up obligation must be resolved within a fixed window after the IPS decision.
    private DateTimeOffset? ReconciliationDeadline(IncomingPayment payment) =>
        payment.FollowUp == IncomingFollowUp.None ? null : payment.IpsDecidedAtUtc!.Value + reconciliationOptions.Window;

    private static DateTimeOffset Earliest(DateTimeOffset first, DateTimeOffset second) => first < second ? first : second;
}
