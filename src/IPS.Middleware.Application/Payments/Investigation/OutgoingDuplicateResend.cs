using IPS.Middleware.Application.Abstractions.Payments;
using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Payments.Pacs008;
using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Payments.Investigation;

// Recovers an outgoing payment whose message type has no investigation (everything but pacs.008): after an unknown
// outcome the exact original message is sent again, flagged as a possible duplicate, so IPS answers with the original's
// status. Each attempt is one-shot per marker; a lost reply schedules the next attempt on the backoff, within the window.
public sealed class OutgoingDuplicateResend(
    IOutgoingPaymentRepository payments,
    ITransactionWorkRepository work,
    IPaymentPreparationRepository preparation,
    IResendRepository resends,
    IUnitOfWork unitOfWork,
    IInvestigationProtocol protocol,
    IIpsTransport transport,
    IIpsReplyInterpreter replies,
    InvestigationOptions options,
    TimeProvider timeProvider)
{
    private const string Exhausted = "IPS did not settle the payment within the recovery window or attempt limit.";

    private readonly ResendExchange _exchange = new(resends, transport, replies, options, timeProvider);

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

        if (!IsRecoverable(payment))
        {
            return payment.Current;
        }

        var latest = await resends.ReadAsync(paymentId, token);
        if (IsWaitingForFirstAttempt(payment, latest))
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
            payment.BeginResending(StatusSource.Recovery, Now);
        }

        try
        {
            await claimed.CommitAsync(token);
            await ContinueAsync(claimed, latest, token);
        }
        catch (PersistenceConcurrencyException)
        {
            // The scope is discarded. Only an outcome committed by this run may be returned.
        }

        return claimed.Committed;
    }

    private async Task ContinueAsync(ClaimedPayment claimed, ResendAttempt? latest, CancellationToken token)
    {
        var stored = (await preparation.ReadAsync(claimed.Payment.Id, token))!;
        var correlation = stored.Accepted!.ReplyCorrelation(stored.MessageId, stored.TransactionId);

        // Saved evidence decides first: a stored reply is interpreted and a marked attempt is never sent again.
        if (latest is { Result: null, Response.Response: { } savedResponse })
        {
            await _exchange.InterpretAsync(RunOf(claimed, latest, correlation), savedResponse, token);
            return;
        }

        if (latest is { Result: null, Request.Submission: not null })
        {
            await _exchange.AbandonAsync(RunOf(claimed, latest, correlation), token);
            return;
        }

        var attempt = latest is { Result: null } ? latest : await StartAttemptAsync(claimed, latest, token);
        if (attempt is null)
        {
            return;
        }

        var run = RunOf(claimed, attempt, correlation);
        if (Now >= run.Deadline)
        {
            await _exchange.StopAtDeadlineAsync(run, token);
            return;
        }

        if (!protocol.MaySend(stored.ReadyDisposition))
        {
            await claimed.ReleaseAsync(Now, Now + options.PreparationRetryDelay, token);
            return;
        }

        if (attempt.Request is null)
        {
            resends.StageReady(claimed.Payment, claimed.Claim, attempt.Id, Now);
            await claimed.CommitAsync(token);
            run = run with { Attempt = (await resends.ReadAsync(claimed.Payment.Id, token))! };
        }

        await _exchange.SendAsync(run, token);
    }

    // The window is frozen when the first attempt is authorized; later attempts reuse it.
    private async Task<ResendAttempt?> StartAttemptAsync(ClaimedPayment claimed, ResendAttempt? previous, CancellationToken token)
    {
        var deadline = previous?.DeadlineUtc ?? claimed.Payment.CreatedAtUtc + options.Window;
        var number = (previous?.Number ?? 0) + 1;
        if (Now >= deadline || CycleLimitReached(number - 1))
        {
            claimed.Payment.RequireManualReview(Now, new PaymentDetails(description: Exhausted));
            await claimed.ReleaseAsync(Now, null, token);
            return null;
        }

        resends.StageAttempt(claimed.Payment, claimed.Claim, number, deadline, Now);
        await claimed.CommitAsync(token);
        return await resends.ReadAsync(claimed.Payment.Id, token);
    }

    // Outcomes are attributed to IPS, and a lost attempt waits its backoff rather than being investigated.
    private static ResendRun RunOf(ClaimedPayment claimed, ResendAttempt attempt, IpsReplyCorrelation correlation) =>
        new(
            claimed,
            attempt,
            correlation,
            attempt.DeadlineUtc!.Value,
            attempt.Number,
            PossibleDuplicate: true,
            SettledBy: StatusSource.Ips,
            FailedBy: StatusSource.Recovery,
            InvestigateAfterAbandon: false);

    private static bool IsRecoverable(OutgoingPayment payment) =>
        PaymentMessageTypes.IsOutgoing(payment.MessageType)
        && !PaymentMessageTypes.HasInvestigation(payment.MessageType)
        && payment.CurrentStatus is TransactionStatus.Uncertain or TransactionStatus.Resending;

    // A fresh uncertain outcome waits FirstDelay before the first attempt; recovery starts at once.
    private bool IsWaitingForFirstAttempt(OutgoingPayment payment, ResendAttempt? latest) =>
        latest is null
        && payment.CurrentStatus == TransactionStatus.Uncertain
        && payment.CurrentSource != StatusSource.Recovery
        && Now < payment.CurrentStatusAtUtc + options.FirstDelay;

    private bool CycleLimitReached(int attempts) => options.MaxCycles > 0 && attempts >= options.MaxCycles;
}
