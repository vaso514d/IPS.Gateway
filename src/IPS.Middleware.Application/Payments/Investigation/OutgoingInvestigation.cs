using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Payments.Investigation;

/// <summary>Resumes one investigation cycle; payment resends are a separate workflow.</summary>
public sealed class OutgoingInvestigation(
    IOutgoingPaymentRepository payments,
    ITransactionWorkRepository work,
    IPaymentPreparationRepository preparation,
    IInvestigationRepository investigations,
    IUnitOfWork unit,
    IInvestigationProtocol protocol,
    IIpsTransport transport,
    InvestigationOptions options,
    TimeProvider time)
{
    public async Task<PaymentOutcome?> ProcessAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        using var budget = new CancellationTokenSource(options.AttemptBudget, time);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        var payment = await payments.FindAsync(paymentId, stop.Token);
        if (payment is null)
        {
            return null;
        }

        var committed = payment.Current;
        if (payment.MessageType != PaymentMessageTypes.Pacs008 ||
            payment.CurrentStatus is not (TransactionStatus.Uncertain or TransactionStatus.Investigating))
        {
            return committed;
        }

        var attempt = await investigations.ReadAsync(paymentId, stop.Token);
        if (attempt?.Result?.Outcome == InvestigationOutcome.NotFound ||
            attempt is null && payment.CurrentSource != StatusSource.Recovery && Now < payment.CurrentStatusAtUtc + options.FirstDelay)
        {
            return committed;
        }

        var claim = work.StageClaim(payment, Now, options.Ownership);
        if (claim is null)
        {
            return committed;
        }

        var execution = new Execution(payment, claim, unit);
        if (payment.CurrentStatus == TransactionStatus.Uncertain)
        {
            payment.BeginInvestigation(Now);
        }

        try
        {
            await execution.CommitAsync(stop.Token);
            await ContinueAsync(execution, attempt, stop.Token);
        }
        catch (PersistenceConcurrencyException)
        {
            // The scope is discarded. Only an outcome committed by this execution may be returned.
        }

        return execution.Committed;
    }

    private async Task ContinueAsync(Execution execution, InvestigationAttempt? attempt, CancellationToken token)
    {
        var stored = await preparation.ReadAsync(execution.Payment.Id, token);
        if (stored?.Accepted is not { } accepted)
        {
            await RequireManualReviewAsync(execution, "Accepted payment data is unavailable.", token);
            return;
        }

        var context = new InvestigationContext(accepted,
            new(stored.MessageId, stored.TransactionId, accepted.Payment.EndToEndId),
            attempt?.Identity.DeadlineUtc ?? accepted.Payment.AcceptanceDateTime + options.Window);

        if (attempt is { Result: null, Response.Response: { } response })
        {
            await InterpretAsync(execution, attempt, context, response, token);
            return;
        }

        if (attempt is { Result: null, Request.Submission: not null })
        {
            await FinishAsync(execution, attempt, context,
                new(InvestigationOutcome.Unresolved, new(description: "Investigation submission was abandoned without saved response.")),
                "Abandoned submission: no saved response.", token);
            return;
        }

        if (Now >= context.Deadline || attempt is { Result: not null } && CycleLimitReached(attempt))
        {
            await RequireManualReviewAsync(execution, "Investigation window or recovery-cycle limit exhausted.", token);
            return;
        }

        attempt = await PrepareAsync(execution, attempt, context, token);
        if (attempt is null)
        {
            return;
        }

        if (attempt.Request!.Disposition == SubmissionMessageKind.DevelopmentUnsigned && !protocol.AllowsDevelopmentUnsigned)
        {
            await ReleaseAsync(execution, Now + options.PreparationRetryDelay, token);
            return;
        }

        if (Now >= context.Deadline)
        {
            await RequireManualReviewAsync(execution, "Investigation window exhausted before dispatch.", token);
            return;
        }

        await SendAsync(execution, attempt, context, token);
    }

    private async Task<InvestigationAttempt?> PrepareAsync(
        Execution execution, InvestigationAttempt? attempt, InvestigationContext context, CancellationToken token)
    {
        if (attempt is null || attempt.Result is not null)
        {
            var identity = new InvestigationIdentity(Guid.NewGuid(), (attempt?.Identity.Number ?? 0) + 1,
                Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), Now, context.Deadline);
            investigations.StageIdentity(execution.Payment, execution.Claim, identity, Now);
            await execution.CommitAsync(token);
            attempt = (await investigations.ReadAsync(execution.Payment.Id, token))!;
        }

        if (attempt.UnsignedXml is null)
        {
            var unsignedXml = protocol.Build(context.Accepted, context.Original, attempt.Identity);
            investigations.StageUnsigned(execution.Payment, execution.Claim, attempt.Identity.Id, unsignedXml, Now);
            await execution.CommitAsync(token);
            attempt = (await investigations.ReadAsync(execution.Payment.Id, token))!;
        }

        if (attempt.Request is null)
        {
            var signing = await protocol.SignAsync(attempt.UnsignedXml!, token);
            if (signing is SigningDeferred)
            {
                await ReleaseAsync(execution, Now + options.PreparationRetryDelay, token);
                return null;
            }

            investigations.StageReady(execution.Payment, execution.Claim, attempt.Identity.Id, (SignedMessage)signing, Now);
            await execution.CommitAsync(token);
            attempt = (await investigations.ReadAsync(execution.Payment.Id, token))!;
        }

        return attempt;
    }

    private async Task SendAsync(Execution execution, InvestigationAttempt attempt, InvestigationContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        investigations.StageSubmission(execution.Payment, execution.Claim, attempt.Identity.Id, Now);
        await execution.CommitAsync(token);

        // A committed marker permits at most this send. Recovery never repeats it.
        var remaining = context.Deadline - Now;
        if (remaining <= TimeSpan.Zero)
        {
            await RequireManualReviewAsync(execution, "Investigation deadline reached after reserving submission.", token);
            return;
        }

        using var callBudget = new CancellationTokenSource(remaining < options.CallTimeout ? remaining : options.CallTimeout, time);
        using var call = CancellationTokenSource.CreateLinkedTokenSource(token, callBudget.Token);
        IpsSubmissionResponse received;
        try
        {
            received = await transport.SendAsync(attempt.Request!.Content, call.Token);
        }
        catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
        {
            using var evidence = new CancellationTokenSource(options.PersistenceBudget, time);
            var failure = $"{error.GetType().FullName}: {error.Message}";
            await FinishAsync(execution, attempt, context,
                new(InvestigationOutcome.Unresolved, new(description: failure)), failure, evidence.Token);
            return;
        }

        using var persistence = new CancellationTokenSource(options.PersistenceBudget, time);
        investigations.StageResponse(execution.Payment, execution.Claim, attempt.Identity.Id, received, Now);
        await execution.CommitAsync(persistence.Token);
        await InterpretAsync(execution, attempt, context, received, persistence.Token);
    }

    private Task InterpretAsync(
        Execution execution, InvestigationAttempt attempt, InvestigationContext context, IpsSubmissionResponse response, CancellationToken token)
    {
        var reply = protocol.Interpret(response, context.Original, attempt.Identity.MessageId);
        return FinishAsync(execution, attempt, context, reply, null, token);
    }

    private async Task FinishAsync(
        Execution execution, InvestigationAttempt attempt, InvestigationContext context, InvestigationReply reply, string? failure, CancellationToken token)
    {
        var payment = execution.Payment;
        investigations.StageResult(payment, execution.Claim, attempt.Identity.Id, reply, failure, Now);
        switch (reply.Outcome)
        {
            case InvestigationOutcome.OriginalAccepted:
                payment.RecordAcceptance(StatusSource.Investigation, Now, reply.Details);
                break;
            case InvestigationOutcome.OriginalRejected:
                payment.RecordRejection(StatusSource.Investigation, Now, reply.Details);
                break;
            default:
                if (reply.Outcome == InvestigationOutcome.Unresolved && (Now >= context.Deadline || CycleLimitReached(attempt)))
                {
                    payment.RequireManualReview(Now, reply.Details);
                }
                else
                {
                    payment.MarkOutcomeUnknown(StatusSource.Investigation, Now, reply.Details);
                }
                break;
        }

        var due = reply.Outcome == InvestigationOutcome.Unresolved && payment.CurrentStatus == TransactionStatus.Uncertain
            ? Now + options.RetryDelay(attempt.Identity.Number) : (DateTimeOffset?)null;
        if (due > context.Deadline)
        {
            due = context.Deadline;
        }

        await ReleaseAsync(execution, due, token);
    }

    private async Task ReleaseAsync(Execution execution, DateTimeOffset? due, CancellationToken token)
    {
        if (!work.StageCompletion(execution.Payment, execution.Claim, Now, due))
        {
            throw new PersistenceConcurrencyException("Investigation ownership expired.");
        }

        await execution.CommitAsync(token);
    }

    private Task RequireManualReviewAsync(Execution execution, string reason, CancellationToken token)
    {
        execution.Payment.RequireManualReview(Now, new(description: reason));
        return ReleaseAsync(execution, null, token);
    }

    private bool CycleLimitReached(InvestigationAttempt attempt) => options.MaxCycles > 0 && attempt.Identity.Number >= options.MaxCycles;
    private DateTimeOffset Now => time.GetUtcNow();

    private sealed class Execution(OutgoingPayment payment, TransactionClaim claim, IUnitOfWork unitOfWork)
    {
        public OutgoingPayment Payment { get; } = payment;
        public TransactionClaim Claim { get; } = claim;
        public PaymentOutcome Committed { get; private set; } = payment.Current;

        public async Task CommitAsync(CancellationToken token)
        {
            await unitOfWork.SaveAsync(token);
            Committed = Payment.Current;
        }
    }

    private sealed class InvestigationContext(AcceptedPacs008 accepted, IpsReplyCorrelation original, DateTimeOffset deadline)
    {
        public AcceptedPacs008 Accepted { get; } = accepted;
        public IpsReplyCorrelation Original { get; } = original;
        public DateTimeOffset Deadline { get; } = deadline;
    }
}
