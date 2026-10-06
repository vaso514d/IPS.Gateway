using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Payments.Investigation;

// Sends the resend a NotFound investigation authorized: the exact original pacs.008, at most once.
// A resend without a stored response is never repeated; the payment is investigated again.
public sealed class OutgoingResend(
    IOutgoingPaymentRepository payments,
    ITransactionWorkRepository work,
    IPaymentPreparationRepository preparation,
    IInvestigationRepository investigations,
    IResendRepository resends,
    IUnitOfWork unitOfWork,
    IInvestigationProtocol protocol,
    IIpsTransport transport,
    IIpsReplyInterpreter replies,
    InvestigationOptions options,
    TimeProvider timeProvider)
{
    private const string AbandonedResend = "Abandoned resend: no saved response.";
    private const string DeadlineReached = "Not resent: the investigation window ended before dispatch.";

    private DateTimeOffset Now => timeProvider.GetUtcNow();

    public async Task<PaymentOutcome?> ProcessAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        using var budget = new CancellationTokenSource(options.AttemptBudget, timeProvider);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        var token = stop.Token;

        var payment = await payments.FindAsync(paymentId, token);
        if (payment is null)
        {
            return null;
        }

        if (!IsResending(payment))
        {
            return payment.Current;
        }

        var claimed = ClaimedPayment.TryStage(payment, work, unitOfWork, Now, options.Ownership);
        if (claimed is null)
        {
            return payment.Current;
        }

        try
        {
            await claimed.CommitAsync(token);
            await ContinueAsync(claimed, token);
        }
        catch (PersistenceConcurrencyException)
        {
            // The scope is discarded. Only an outcome committed by this run may be returned.
        }

        return claimed.Committed;
    }

    private async Task ContinueAsync(ClaimedPayment claimed, CancellationToken token)
    {
        var paymentId = claimed.Payment.Id;
        var resend = (await resends.ReadAsync(paymentId, token))!;
        var investigation = (await investigations.ReadAsync(paymentId, token))!;
        var stored = (await preparation.ReadAsync(paymentId, token))!;
        var context = new ResendContext(
            new IpsReplyCorrelation(stored.MessageId, stored.TransactionId, stored.Accepted!.Payment.EndToEndId),
            investigation.Identity.Number,
            investigation.Identity.DeadlineUtc);

        if (resend.Response?.Response is { } savedResponse)
        {
            await InterpretAsync(claimed, resend, context, savedResponse, token);
            return;
        }

        if (resend.Request?.Submission is not null)
        {
            await AbandonAsync(claimed, resend, token);
            return;
        }

        if (Now >= context.Deadline)
        {
            await StopAtDeadlineAsync(claimed, resend, token);
            return;
        }

        if (!protocol.MaySend(stored.ReadyDisposition))
        {
            await claimed.ReleaseAsync(Now, Now + options.PreparationRetryDelay, token);
            return;
        }

        if (resend.Request is null)
        {
            resends.StageReady(claimed.Payment, claimed.Claim, resend.Id, Now);
            await claimed.CommitAsync(token);
            resend = (await resends.ReadAsync(paymentId, token))!;
        }

        await SendAsync(claimed, resend, context, token);
    }

    private async Task SendAsync(ClaimedPayment claimed, ResendAttempt resend, ResendContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        resends.StageSubmission(claimed.Payment, claimed.Claim, resend.Id, Now);
        await claimed.CommitAsync(token);

        // A committed marker permits at most this send. Recovery never repeats it.
        var remaining = context.Deadline - Now;
        if (remaining <= TimeSpan.Zero)
        {
            await StopAtDeadlineAsync(claimed, resend, token);
            return;
        }

        using var callBudget = new CancellationTokenSource(options.CallBudget(remaining), timeProvider);
        using var call = CancellationTokenSource.CreateLinkedTokenSource(token, callBudget.Token);
        IpsSubmissionResponse received;
        try
        {
            received = await transport.SendAsync(resend.Request!.Content, call.Token);
        }
        catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
        {
            using var evidence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
            var failure = $"{error.GetType().FullName}: {error.Message}";
            resends.StageResult(claimed.Payment, claimed.Claim, resend.Id, Unresolved(failure), failure, Now);
            claimed.Payment.MarkOutcomeUnknown(StatusSource.Investigation, Now, new PaymentDetails(description: failure));
            await claimed.ReleaseAsync(Now, NextInvestigationAt(context), evidence.Token);
            return;
        }

        using var persistence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
        resends.StageResponse(claimed.Payment, claimed.Claim, resend.Id, received, Now);
        await claimed.CommitAsync(persistence.Token);
        await InterpretAsync(claimed, resend, context, received, persistence.Token);
    }

    // Source Investigation, as for every outcome the investigation workflow records.
    private Task InterpretAsync(
        ClaimedPayment claimed,
        ResendAttempt resend,
        ResendContext context,
        IpsSubmissionResponse response,
        CancellationToken token)
    {
        var payment = claimed.Payment;
        var reply = replies.Interpret(response, context.Original);
        resends.StageResult(payment, claimed.Claim, resend.Id, reply, null, Now);
        switch (reply.Status)
        {
            case IpsReplyStatus.Accepted:
                payment.RecordAcceptance(StatusSource.Investigation, Now, reply.Details);
                break;
            case IpsReplyStatus.Rejected:
                payment.RecordRejection(StatusSource.Investigation, Now, reply.Details);
                break;
            default:
                payment.MarkOutcomeUnknown(StatusSource.Investigation, Now, reply.Details);
                break;
        }

        var next = payment.CurrentStatus == TransactionStatus.Uncertain ? NextInvestigationAt(context) : (DateTimeOffset?)null;
        return claimed.ReleaseAsync(Now, next, token);
    }

    // The previous owner may have sent it; like any abandoned send, the payment is investigated at once.
    private Task AbandonAsync(ClaimedPayment claimed, ResendAttempt resend, CancellationToken token)
    {
        resends.StageResult(claimed.Payment, claimed.Claim, resend.Id, Unresolved(AbandonedResend), AbandonedResend, Now);
        claimed.Payment.MarkOutcomeUnknown(StatusSource.Recovery, Now, new PaymentDetails(description: AbandonedResend));
        return claimed.ReleaseAsync(Now, Now, token);
    }

    // Nothing was dispatched, so no transport failure is recorded.
    private Task StopAtDeadlineAsync(ClaimedPayment claimed, ResendAttempt resend, CancellationToken token)
    {
        resends.StageResult(claimed.Payment, claimed.Claim, resend.Id, Unresolved(DeadlineReached), null, Now);
        claimed.Payment.RequireManualReview(Now, new PaymentDetails(description: DeadlineReached));
        return claimed.ReleaseAsync(Now, null, token);
    }

    // The next investigation follows the cycle schedule, never past the window.
    private DateTimeOffset NextInvestigationAt(ResendContext context)
    {
        var next = Now + options.RetryDelay(context.InvestigationNumber);
        return next > context.Deadline ? context.Deadline : next;
    }

    private static IpsReply Unresolved(string description) =>
        new(IpsReplyStatus.Unresolved, new PaymentDetails(description: description));

    private static bool IsResending(OutgoingPayment payment) =>
        payment.MessageType == PaymentMessageTypes.Pacs008
        && payment.CurrentStatus == TransactionStatus.Resending;

    private sealed record ResendContext(IpsReplyCorrelation Original, int InvestigationNumber, DateTimeOffset Deadline);
}
