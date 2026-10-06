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

        // Source Investigation, as for every outcome the investigation workflow records. A resend that may have been
        // sent without a saved reply is investigated again at once; an unknown reply follows the cycle schedule.
        var run = new ResendRun(
            claimed,
            resend,
            new IpsReplyCorrelation(stored.MessageId, stored.TransactionId, stored.Accepted!.EndToEndId),
            investigation.Identity.DeadlineUtc,
            investigation.Identity.Number,
            PossibleDuplicate: false,
            SettledBy: StatusSource.Investigation,
            FailedBy: StatusSource.Investigation,
            InvestigateAfterAbandon: true);

        if (resend.Response?.Response is { } savedResponse)
        {
            await _exchange.InterpretAsync(run, savedResponse, token);
            return;
        }

        if (resend.Request?.Submission is not null)
        {
            await _exchange.AbandonAsync(run, token);
            return;
        }

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

        if (resend.Request is null)
        {
            resends.StageReady(claimed.Payment, claimed.Claim, resend.Id, Now);
            await claimed.CommitAsync(token);
            run = run with { Attempt = (await resends.ReadAsync(paymentId, token))! };
        }

        await _exchange.SendAsync(run, token);
    }

    private static bool IsResending(OutgoingPayment payment) =>
        PaymentMessageTypes.HasInvestigation(payment.MessageType)
        && payment.CurrentStatus == TransactionStatus.Resending;
}
