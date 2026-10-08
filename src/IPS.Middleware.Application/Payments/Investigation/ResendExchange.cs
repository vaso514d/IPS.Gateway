using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Payments.Investigation;

// The steps both resend workflows share. One committed marker permits one send, and a saved reply is interpreted
// before anything else; each workflow decides only when a resend is authorized and what follows an unknown result.
internal sealed class ResendExchange(
    IResendRepository resends,
    IIpsTransport transport,
    IIpsReplyInterpreter replies,
    InvestigationOptions options,
    TimeProvider timeProvider)
{
    private const string AbandonedResend = "Abandoned resend: no saved response.";
    private const string DeadlineReached = "Not resent: the window ended before dispatch.";

    private DateTimeOffset Now => timeProvider.GetUtcNow();

    public async Task SendAsync(ResendRun run, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var claimed = run.Claimed;
        resends.StageSubmission(claimed.Payment, claimed.Claim, run.Attempt.Id, Now);
        await claimed.CommitAsync(token);

        // A committed marker permits at most this send. Recovery never repeats it.
        var remaining = run.Deadline - Now;
        if (remaining <= TimeSpan.Zero)
        {
            await StopAtDeadlineAsync(run, token);
            return;
        }

        using var callBudget = new CancellationTokenSource(options.CallBudget(remaining), timeProvider);
        using var call = CancellationTokenSource.CreateLinkedTokenSource(token, callBudget.Token);
        IpsSubmissionResponse received;
        try
        {
            received = run.PossibleDuplicate
                ? await transport.ResendAsync(run.Attempt.Request!.Content, call.Token)
                : await transport.SendAsync(run.Attempt.Request!.Content, call.Token);
        }
        catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
        {
            using var evidence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
            var failure = $"{error.GetType().FullName}: {error.Message}";
            resends.StageResult(claimed.Payment, claimed.Claim, run.Attempt.Id, Unresolved(failure), failure, Now);
            claimed.Payment.MarkOutcomeUnknown(run.FailedBy, Now, new PaymentDetails(description: failure));
            await claimed.ReleaseAsync(Now, NextAttemptAt(run), evidence.Token);
            return;
        }

        using var persistence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
        resends.StageResponse(claimed.Payment, claimed.Claim, run.Attempt.Id, received, Now);
        await claimed.CommitAsync(persistence.Token);
        await InterpretAsync(run, received, persistence.Token);
    }

    public Task InterpretAsync(ResendRun run, IpsSubmissionResponse response, CancellationToken token)
    {
        var payment = run.Claimed.Payment;
        var reply = replies.Interpret(response, run.Correlation);
        resends.StageResult(payment, run.Claimed.Claim, run.Attempt.Id, reply, null, Now);
        switch (reply.Status)
        {
            case IpsReplyStatus.Accepted:
                payment.RecordAcceptance(run.SettledBy, Now, reply.Details);
                break;
            case IpsReplyStatus.Rejected:
                payment.RecordRejection(run.SettledBy, Now, reply.Details);
                break;
            default:
                payment.MarkOutcomeUnknown(run.SettledBy, Now, reply.Details);
                break;
        }

        var next = payment.CurrentStatus == TransactionStatus.Uncertain ? NextAttemptAt(run) : (DateTimeOffset?)null;
        return run.Claimed.ReleaseAsync(Now, next, token);
    }

    // The previous owner may have sent it, so the outcome is unknown again; the workflow says when to look again.
    public Task AbandonAsync(ResendRun run, CancellationToken token)
    {
        var claimed = run.Claimed;
        resends.StageResult(claimed.Payment, claimed.Claim, run.Attempt.Id, Unresolved(AbandonedResend), AbandonedResend, Now);
        claimed.Payment.MarkOutcomeUnknown(StatusSource.Recovery, Now, new PaymentDetails(description: AbandonedResend));
        return claimed.ReleaseAsync(Now, run.InvestigateAfterAbandon ? Now : NextAttemptAt(run), token);
    }

    // Nothing was dispatched, so no transport failure is recorded.
    public Task StopAtDeadlineAsync(ResendRun run, CancellationToken token)
    {
        var claimed = run.Claimed;
        resends.StageResult(claimed.Payment, claimed.Claim, run.Attempt.Id, Unresolved(DeadlineReached), null, Now);
        claimed.Payment.RequireManualReview(Now, new PaymentDetails(description: DeadlineReached));
        return claimed.ReleaseAsync(Now, null, token);
    }

    // The next look follows the backoff schedule, never past the window.
    private DateTimeOffset NextAttemptAt(ResendRun run)
    {
        var next = Now + options.RetryDelay(run.Cycle);
        return next > run.Deadline ? run.Deadline : next;
    }

    private static IpsReply Unresolved(string description) =>
        new(IpsReplyStatus.Unresolved, new PaymentDetails(description: description));
}

// One resend in progress: what is sent, how its reply is judged, and how its results are attributed and scheduled.
internal sealed record ResendRun(
    ClaimedPayment Claimed,
    ResendAttempt Attempt,
    IpsReplyCorrelation Correlation,
    DateTimeOffset Deadline,
    int Cycle,
    bool PossibleDuplicate,
    StatusSource SettledBy,
    StatusSource FailedBy,
    bool InvestigateAfterAbandon);
