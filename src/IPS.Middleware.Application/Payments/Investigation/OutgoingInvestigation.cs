using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Payments.Investigation;

/// <summary>Resumes one investigation cycle; payment resends are a separate workflow.</summary>
public sealed class OutgoingInvestigation(IOutgoingPaymentRepository payments, ITransactionWorkRepository work,
    IPaymentPreparationRepository preparation, IInvestigationRepository investigations, IUnitOfWork unit,
    IInvestigationProtocol protocol, IIpsTransport transport, InvestigationOptions options, TimeProvider time)
{
    public async Task<PaymentOutcome?> ProcessAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        using var budget = new CancellationTokenSource(options.AttemptBudget, time);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        var token = stop.Token;
        var payment = await payments.FindAsync(paymentId, token);
        if (payment is null) return null;
        var committed = payment.Current;
        if (payment.MessageType != PaymentMessageTypes.Pacs008 || payment.CurrentStatus is not (TransactionStatus.Uncertain or TransactionStatus.Investigating)) return committed;
        var attempt = await investigations.ReadAsync(paymentId, token);
        if (attempt?.Result?.Outcome == InvestigationOutcome.NotFound) return committed;
        if (attempt is null && payment.CurrentSource != StatusSource.Recovery && Now < payment.CurrentStatusAtUtc + options.FirstDelay) return committed;
        var claim = work.StageClaim(payment, Now, options.Ownership);
        if (claim is null) return committed;
        if (payment.CurrentStatus == TransactionStatus.Uncertain) payment.BeginInvestigation(Now);
        async Task Commit(CancellationToken ct) { await unit.SaveAsync(ct); committed = payment.Current; }
        try
        {
            await Commit(token);
            var stored = await preparation.ReadAsync(paymentId, token);
            if (stored?.Accepted is not { } accepted)
            {
                await Manual("Accepted payment data is unavailable.", token); return committed;
            }
            var deadline = attempt?.Identity.DeadlineUtc ?? accepted.Payment.AcceptanceDateTime + options.Window;
            var original = new IpsReplyCorrelation(stored.MessageId, stored.TransactionId, accepted.Payment.EndToEndId);
            if (attempt is { Result: null, Response.Response: { } response })
            {
                await Interpret(attempt, response, token); return committed;
            }
            if (attempt is { Result: null, Request.Submission: not null })
            {
                await Finish(attempt, new(InvestigationOutcome.Unresolved, new(description: "Investigation submission was abandoned without saved response.")),
                    "Abandoned submission: no saved response.", token); return committed;
            }
            if (Now >= deadline || attempt is { Result: not null } && options.MaxCycles > 0 && attempt.Identity.Number >= options.MaxCycles)
            {
                await Manual("Investigation window or recovery-cycle limit exhausted.", token); return committed;
            }
            if (attempt is null || attempt.Result is not null)
            {
                var identity = new InvestigationIdentity(Guid.NewGuid(), (attempt?.Identity.Number ?? 0) + 1,
                    Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), Now, deadline);
                investigations.StageIdentity(payment, claim, identity, Now); await Commit(token);
                attempt = (await investigations.ReadAsync(paymentId, token))!;
            }
            if (attempt.UnsignedXml is null)
            {
                investigations.StageUnsigned(payment, claim, attempt.Identity.Id, protocol.Build(accepted, original, attempt.Identity), Now);
                await Commit(token); attempt = (await investigations.ReadAsync(paymentId, token))!;
            }
            if (attempt.Request is null)
            {
                var signing = await protocol.SignAsync(attempt.UnsignedXml!, token);
                if (signing is SigningDeferred)
                {
                    await Release(Now + options.PreparationRetryDelay, token); return committed;
                }
                investigations.StageReady(payment, claim, attempt.Identity.Id, (SignedMessage)signing, Now);
                await Commit(token); attempt = (await investigations.ReadAsync(paymentId, token))!;
            }
            if (attempt.Request!.Disposition == SubmissionMessageKind.DevelopmentUnsigned && !protocol.AllowsDevelopmentUnsigned)
            {
                await Release(Now + options.PreparationRetryDelay, token); return committed;
            }
            if (Now >= deadline) { await Manual("Investigation window exhausted before dispatch.", token); return committed; }
            token.ThrowIfCancellationRequested();
            investigations.StageSubmission(payment, claim, attempt.Identity.Id, Now); await Commit(token);
            // A committed marker permits at most this send. Recovery never repeats it.
            IpsSubmissionResponse received;
            var remaining = deadline - Now;
            if (remaining <= TimeSpan.Zero) { await Manual("Investigation deadline reached after reserving submission.", token); return committed; }
            using var callBudget = new CancellationTokenSource(remaining < options.CallTimeout ? remaining : options.CallTimeout, time);
            using var call = CancellationTokenSource.CreateLinkedTokenSource(token, callBudget.Token);
            try { received = await transport.SendAsync(attempt.Request.Content, call.Token); }
            catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
            {
                using var evidence = new CancellationTokenSource(options.PersistenceBudget, time);
                var failure = $"{error.GetType().FullName}: {error.Message}";
                await Finish(attempt, new(InvestigationOutcome.Unresolved, new(description: failure)), failure, evidence.Token);
                return committed;
            }
            using (var evidence = new CancellationTokenSource(options.PersistenceBudget, time))
            {
                investigations.StageResponse(payment, claim, attempt.Identity.Id, received, Now); await Commit(evidence.Token);
                await Interpret(attempt, received, evidence.Token);
            }
            return committed;

            Task Interpret(InvestigationAttempt current, IpsSubmissionResponse response, CancellationToken ct) =>
                Finish(current, protocol.Interpret(response, original, current.Identity.MessageId), null, ct);
            async Task Finish(InvestigationAttempt current, InvestigationReply reply, string? failure, CancellationToken ct)
            {
                investigations.StageResult(payment, claim, current.Identity.Id, reply, failure, Now);
                switch (reply.Outcome)
                {
                    case InvestigationOutcome.OriginalAccepted: payment.RecordAcceptance(StatusSource.Investigation, Now, reply.Details); break;
                    case InvestigationOutcome.OriginalRejected: payment.RecordRejection(StatusSource.Investigation, Now, reply.Details); break;
                    default:
                        if (reply.Outcome == InvestigationOutcome.Unresolved && (Now >= deadline || options.MaxCycles > 0 && current.Identity.Number >= options.MaxCycles))
                            payment.RequireManualReview(Now, reply.Details);
                        else payment.MarkOutcomeUnknown(StatusSource.Investigation, Now, reply.Details);
                        break;
                }
                var due = reply.Outcome == InvestigationOutcome.Unresolved && payment.CurrentStatus == TransactionStatus.Uncertain
                    ? Now + options.RetryDelay(current.Identity.Number) : (DateTimeOffset?)null;
                if (due > deadline) due = deadline;
                await Release(due, ct);
            }
        }
        catch (PersistenceConcurrencyException) { return committed; }

        async Task Release(DateTimeOffset? due, CancellationToken ct)
        {
            if (!work.StageCompletion(payment, claim, Now, due)) throw new PersistenceConcurrencyException("Investigation ownership expired.");
            await Commit(ct);
        }
        async Task Manual(string reason, CancellationToken ct)
        {
            payment.RequireManualReview(Now, new(description: reason)); await Release(null, ct);
        }
    }
    private DateTimeOffset Now => time.GetUtcNow();
}
