using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Payments.Investigation;

// Resumes one investigation cycle. A NotFound result authorizes a resend, which OutgoingResend sends.
public sealed class OutgoingInvestigation(
    IOutgoingPaymentRepository payments,
    ITransactionWorkRepository work,
    IPaymentPreparationRepository preparation,
    IInvestigationRepository investigations,
    IResendRepository resends,
    IUnitOfWork unitOfWork,
    IInvestigationProtocol protocol,
    IIpsTransport transport,
    InvestigationOptions options,
    TimeProvider timeProvider)
{
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

        if (!IsInvestigable(payment))
        {
            return payment.Current;
        }

        var attempt = await investigations.ReadAsync(paymentId, token);
        if (IsWaitingForFirstCycle(payment, attempt))
        {
            return payment.Current;
        }

        var claimed = ClaimedPayment.TryStage(payment, work, unitOfWork, Now, options.Ownership);
        if (claimed is null)
        {
            return payment.Current;
        }

        if (payment.CurrentStatus == TransactionStatus.Uncertain)
        {
            payment.BeginInvestigation(Now);
        }

        try
        {
            await claimed.CommitAsync(token);
            await ContinueAsync(claimed, attempt, token);
        }
        catch (PersistenceConcurrencyException)
        {
            // The scope is discarded. Only an outcome committed by this run may be returned.
        }

        return claimed.Committed;
    }

    private async Task ContinueAsync(ClaimedPayment claimed, InvestigationAttempt? attempt, CancellationToken token)
    {
        var stored = await preparation.ReadAsync(claimed.Payment.Id, token);
        if (stored?.Accepted is not { } accepted)
        {
            await RequireManualReviewAsync(claimed, "Accepted payment data is unavailable.", token);
            return;
        }

        var context = new InvestigationContext(
            accepted,
            new IpsReplyCorrelation(stored.MessageId, stored.TransactionId, accepted.Payment.EndToEndId),
            attempt?.Identity.DeadlineUtc ?? accepted.Payment.AcceptanceDateTime + options.Window);

        if (attempt is { Result: null, Response.Response: { } savedResponse })
        {
            await InterpretAsync(claimed, attempt, context, savedResponse, token);
            return;
        }

        if (attempt is { Result: null, Request.Submission: not null })
        {
            const string abandoned = "Abandoned submission: no saved response.";
            var reply = new InvestigationReply(
                InvestigationOutcome.Unresolved,
                new PaymentDetails(description: "Investigation submission was abandoned without saved response."));
            await FinishAsync(claimed, attempt, context, reply, abandoned, token);
            return;
        }

        if (Now >= context.Deadline || attempt is { Result: not null } && CycleLimitReached(attempt))
        {
            await RequireManualReviewAsync(claimed, "Investigation window or recovery-cycle limit exhausted.", token);
            return;
        }

        var prepared = await PrepareAsync(claimed, attempt, context, token);
        if (prepared is null)
        {
            return;
        }

        if (!protocol.MaySend(prepared.Request!.Disposition))
        {
            await claimed.ReleaseAsync(Now, Now + options.PreparationRetryDelay, token);
            return;
        }

        if (Now >= context.Deadline)
        {
            await RequireManualReviewAsync(claimed, "Investigation window exhausted before dispatch.", token);
            return;
        }

        await SendAsync(claimed, prepared, context, token);
    }

    // Each step commits before the next and reloads the attempt, so a restart resumes from the stored checkpoint.
    private async Task<InvestigationAttempt?> PrepareAsync(
        ClaimedPayment claimed,
        InvestigationAttempt? attempt,
        InvestigationContext context,
        CancellationToken token)
    {
        if (attempt is null || attempt.Result is not null)
        {
            var identity = new InvestigationIdentity(
                Guid.NewGuid(),
                (attempt?.Identity.Number ?? 0) + 1,
                Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N"),
                Now,
                context.Deadline);
            investigations.StageIdentity(claimed.Payment, claimed.Claim, identity, Now);
            attempt = await CommitAndReloadAsync(claimed, token);
        }

        if (attempt.UnsignedXml is null)
        {
            var unsignedXml = protocol.Build(context.Accepted, context.Original, attempt.Identity);
            investigations.StageUnsigned(claimed.Payment, claimed.Claim, attempt.Identity.Id, unsignedXml, Now);
            attempt = await CommitAndReloadAsync(claimed, token);
        }

        if (attempt.Request is null)
        {
            var signing = await protocol.SignAsync(attempt.UnsignedXml!, token);
            if (signing is SigningDeferred)
            {
                await claimed.ReleaseAsync(Now, Now + options.PreparationRetryDelay, token);
                return null;
            }

            investigations.StageReady(claimed.Payment, claimed.Claim, attempt.Identity.Id, (SignedMessage)signing, Now);
            attempt = await CommitAndReloadAsync(claimed, token);
        }

        return attempt;
    }

    private async Task SendAsync(ClaimedPayment claimed, InvestigationAttempt attempt, InvestigationContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        investigations.StageSubmission(claimed.Payment, claimed.Claim, attempt.Identity.Id, Now);
        await claimed.CommitAsync(token);

        // A committed marker permits at most this send. Recovery never repeats it.
        var remaining = context.Deadline - Now;
        if (remaining <= TimeSpan.Zero)
        {
            await RequireManualReviewAsync(claimed, "Investigation deadline reached after reserving submission.", token);
            return;
        }

        using var callBudget = new CancellationTokenSource(options.CallBudget(remaining), timeProvider);
        using var call = CancellationTokenSource.CreateLinkedTokenSource(token, callBudget.Token);
        IpsSubmissionResponse received;
        try
        {
            received = await transport.SendAsync(attempt.Request!.Content, call.Token);
        }
        catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
        {
            using var evidence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
            var failure = $"{error.GetType().FullName}: {error.Message}";
            var unresolved = new InvestigationReply(InvestigationOutcome.Unresolved, new PaymentDetails(description: failure));
            await FinishAsync(claimed, attempt, context, unresolved, failure, evidence.Token);
            return;
        }

        using var persistence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
        investigations.StageResponse(claimed.Payment, claimed.Claim, attempt.Identity.Id, received, Now);
        await claimed.CommitAsync(persistence.Token);
        await InterpretAsync(claimed, attempt, context, received, persistence.Token);
    }

    private Task InterpretAsync(
        ClaimedPayment claimed,
        InvestigationAttempt attempt,
        InvestigationContext context,
        IpsSubmissionResponse response,
        CancellationToken token)
    {
        var reply = protocol.Interpret(response, context.Original, attempt.Identity.MessageId);
        return FinishAsync(claimed, attempt, context, reply, null, token);
    }

    private async Task FinishAsync(
        ClaimedPayment claimed,
        InvestigationAttempt attempt,
        InvestigationContext context,
        InvestigationReply reply,
        string? transportFailure,
        CancellationToken token)
    {
        var payment = claimed.Payment;
        investigations.StageResult(payment, claimed.Claim, attempt.Identity.Id, reply, transportFailure, Now);
        if (reply.Outcome == InvestigationOutcome.NotFound)
        {
            await AuthorizeResendAsync(claimed, attempt, reply, token);
            return;
        }

        switch (reply.Outcome)
        {
            case InvestigationOutcome.OriginalAccepted:
                payment.RecordAcceptance(StatusSource.Investigation, Now, reply.Details);
                break;
            case InvestigationOutcome.OriginalRejected:
                payment.RecordRejection(StatusSource.Investigation, Now, reply.Details);
                break;
            case InvestigationOutcome.Unresolved when Now >= context.Deadline || CycleLimitReached(attempt):
                payment.RequireManualReview(Now, reply.Details);
                break;
            default:
                payment.MarkOutcomeUnknown(StatusSource.Investigation, Now, reply.Details);
                break;
        }

        await claimed.ReleaseAsync(Now, NextCycleAt(payment, reply, attempt, context.Deadline), token);
    }

    // IPS has no record of the payment: one resend per NotFound result, counted separately from investigation cycles.
    private async Task AuthorizeResendAsync(ClaimedPayment claimed, InvestigationAttempt attempt, InvestigationReply reply, CancellationToken token)
    {
        var previous = await resends.ReadAsync(claimed.Payment.Id, token);
        var resent = previous?.Number ?? 0;
        if (resent >= options.MaxResends)
        {
            await RequireManualReviewAsync(claimed, $"IPS has no record of the transaction and it was already resent {resent} time(s).", token);
            return;
        }

        claimed.Payment.BeginResending(StatusSource.Investigation, Now, reply.Details);
        resends.StageAuthorization(claimed.Payment, claimed.Claim, attempt.Identity.Id, resent + 1, Now);
        await claimed.ReleaseAsync(Now, null, token);
    }

    // Only an unresolved cycle that left the payment uncertain schedules another, never past the window.
    private DateTimeOffset? NextCycleAt(OutgoingPayment payment, InvestigationReply reply, InvestigationAttempt attempt, DateTimeOffset deadline)
    {
        if (reply.Outcome != InvestigationOutcome.Unresolved || payment.CurrentStatus != TransactionStatus.Uncertain)
        {
            return null;
        }

        var next = Now + options.RetryDelay(attempt.Identity.Number);
        return next > deadline ? deadline : next;
    }

    private Task RequireManualReviewAsync(ClaimedPayment claimed, string reason, CancellationToken token)
    {
        claimed.Payment.RequireManualReview(Now, new PaymentDetails(description: reason));
        return claimed.ReleaseAsync(Now, null, token);
    }

    private async Task<InvestigationAttempt> CommitAndReloadAsync(ClaimedPayment claimed, CancellationToken token)
    {
        await claimed.CommitAsync(token);
        return (await investigations.ReadAsync(claimed.Payment.Id, token))!;
    }

    private static bool IsInvestigable(OutgoingPayment payment) =>
        payment.MessageType == PaymentMessageTypes.Pacs008
        && payment.CurrentStatus is TransactionStatus.Uncertain or TransactionStatus.Investigating;

    // A fresh uncertain outcome waits FirstDelay before the first cycle; recovery starts at once.
    private bool IsWaitingForFirstCycle(OutgoingPayment payment, InvestigationAttempt? attempt) =>
        attempt is null
        && payment.CurrentSource != StatusSource.Recovery
        && Now < payment.CurrentStatusAtUtc + options.FirstDelay;

    private bool CycleLimitReached(InvestigationAttempt attempt) =>
        options.MaxCycles > 0 && attempt.Identity.Number >= options.MaxCycles;

    private sealed record InvestigationContext(AcceptedPacs008 Accepted, IpsReplyCorrelation Original, DateTimeOffset Deadline);
}
