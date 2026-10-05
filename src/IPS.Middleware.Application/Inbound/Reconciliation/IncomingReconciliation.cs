using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Reconciliation;

// Resolves a rejected payment's open CBS obligation: query an unknown outcome, or request reversal of a credit.
public sealed class IncomingReconciliation(
    IIncomingReconciliationRepository work,
    IIncomingProcessingRepository processing,
    IUnitOfWork unitOfWork,
    IIncomingCoreClient core,
    IIncomingReversalClient reversals,
    IIncomingCoreReplyInterpreter replies,
    IncomingReconciliationOptions options,
    TimeProvider timeProvider)
{
    private const string WindowExpired = "The fixed CBS reconciliation window expired.";

    private DateTimeOffset Now => timeProvider.GetUtcNow();

    public Task<IReadOnlyList<Guid>> DiscoverAsync(CancellationToken token) => work.FindDueAsync(Now, options.DiscoveryBatch, token);

    public async Task<IncomingProcessingResult?> ProcessAsync(Guid paymentId, CancellationToken token)
    {
        var snapshot = await processing.ReadAsync(paymentId, token);
        if (snapshot is null)
        {
            return null;
        }

        ClaimedIncomingPayment? claimed = null;
        try
        {
            var claim = await work.StageClaimAsync(paymentId, Now, options.Ownership, token);
            if (claim is null)
            {
                return IncomingProcessingResult.Of(snapshot.Payment);
            }

            claimed = new ClaimedIncomingPayment(snapshot, claim, unitOfWork);
            await claimed.CommitAsync(token);
            await ContinueAsync(claimed, token);
        }
        catch (PersistenceConcurrencyException)
        {
            // Discard this scope; never replay remote effects on a failed unit of work.
        }

        return claimed?.Committed ?? IncomingProcessingResult.Of(snapshot.Payment);
    }

    private async Task ContinueAsync(ClaimedIncomingPayment claimed, CancellationToken token)
    {
        var payment = claimed.Payment;
        var deadline = claimed.Snapshot.ReconciliationDeadlineUtc
            ?? throw new InvalidOperationException("Follow-up work requires a frozen reconciliation deadline.");
        var reconciliationAttempts = claimed.Snapshot.Calls.Count(call => call.Kind == CoreCallKind.Reconciliation);

        var replayed = false;
        foreach (var call in claimed.Snapshot.Calls.Where(IsUnconsumedFollowUpResult))
        {
            await InterpretAsync(claimed, call, token);
            replayed = true;
        }

        // A reversal marker may mean the request already changed CBS, even when no reply was stored.
        if (payment.Reversal == ReversalDelivery.Started)
        {
            payment.RecordReversalDelivery(ReversalDelivery.Uncertain, Now);
        }

        if (await FinishIfClosedAsync(claimed, deadline, token))
        {
            return;
        }

        // Replayed results and their resulting schedule commit before any further remote effect.
        if (!replayed && await CallAsync(claimed, deadline, token) == CoreCallKind.Reconciliation)
        {
            reconciliationAttempts++;
        }

        if (await FinishIfClosedAsync(claimed, deadline, token))
        {
            return;
        }

        var next = payment.FollowUp == IncomingFollowUp.ReversalRequired ? Now : Now + options.RetryDelay(reconciliationAttempts);
        await ReleaseAsync(claimed, next < deadline ? next : deadline, token);
    }

    private async Task<CoreCallKind> CallAsync(ClaimedIncomingPayment claimed, DateTimeOffset deadline, CancellationToken token)
    {
        var payment = claimed.Payment;
        var kind = payment.FollowUp == IncomingFollowUp.ReversalRequired ? CoreCallKind.Reversal : CoreCallKind.Reconciliation;
        var notification = kind == CoreCallKind.Reversal ? BeginReversal(claimed) : null;
        var call = await processing.StageCallAsync(claimed.Claim, kind, Now, token, notification);
        await claimed.CommitAsync(token);
        if (!await processing.IsOwnerAsync(claimed.Claim, Now, token))
        {
            throw new PersistenceConcurrencyException("Reconciliation owner was lost before dispatch.");
        }

        token.ThrowIfCancellationRequested();
        var remaining = deadline - Now;
        var callBudget = remaining < options.CallTimeout ? remaining : options.CallTimeout;
        if (callBudget <= TimeSpan.Zero)
        {
            if (kind == CoreCallKind.Reversal)
            {
                payment.RecordReversalDelivery(ReversalDelivery.Uncertain, Now);
            }

            return kind;
        }

        var completion = await CoreCallExecution.ExecuteAsync(callToken => SendAsync(claimed.Snapshot, call, callToken), callBudget, timeProvider, token);
        using var persistence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
        await processing.StageCompletionAsync(claimed.Claim, call.Id, completion, Now, persistence.Token);
        await claimed.CommitAsync(persistence.Token);
        token.ThrowIfCancellationRequested();
        await InterpretAsync(claimed, call with { Completion = completion }, token);
        return kind;
    }

    private Task<CoreResponse> SendAsync(IncomingProcessingSnapshot snapshot, IncomingCoreCall call, CancellationToken callToken) =>
        call.Kind == CoreCallKind.Reversal
            ? reversals.RequestAsync(call.Notification!, callToken)
            : core.QueryAsync(snapshot.Payment.ParticipantBic, snapshot.Payment.EndToEndId, callToken);

    private ReversalNotification BeginReversal(ClaimedIncomingPayment claimed)
    {
        var payment = claimed.Payment;
        var decision = payment.IpsDecision!;
        var notification = new ReversalNotification(
            payment.Id,
            payment.ParticipantBic,
            payment.EndToEndId,
            decision.DecidedAtUtc,
            decision.ReasonCode,
            decision.Description,
            claimed.Snapshot.Context.Original.GroupMessageId);
        payment.BeginReversal(Now);
        return notification;
    }

    private async Task<bool> FinishIfClosedAsync(ClaimedIncomingPayment claimed, DateTimeOffset deadline, CancellationToken token)
    {
        var payment = claimed.Payment;
        if (payment.FollowUp is IncomingFollowUp.None or IncomingFollowUp.ManualReviewRequired)
        {
            await ReleaseAsync(claimed, null, token);
            return true;
        }

        if (Now >= deadline)
        {
            payment.RequireManualReview(WindowExpired, Now);
            await ReleaseAsync(claimed, null, token);
            return true;
        }

        return false;
    }

    private async Task InterpretAsync(ClaimedIncomingPayment claimed, IncomingCoreCall call, CancellationToken token)
    {
        await processing.StageConsumptionAsync(claimed.Claim, call.Id, Now, token);
        var completion = call.Completion!;
        if (call.Kind == CoreCallKind.Reversal)
        {
            claimed.Payment.RecordReversalDelivery(ReversalOutcome(completion), completion.ObservedAtUtc);
        }
        else
        {
            claimed.Payment.RecordCoreResult(replies.Interpret(completion, claimed.Snapshot.Context.Original), Now);
        }
    }

    private async Task ReleaseAsync(ClaimedIncomingPayment claimed, DateTimeOffset? nextActionAtUtc, CancellationToken token)
    {
        await work.StageReleaseAsync(claimed.Claim, Now, nextActionAtUtc, token);
        await claimed.CommitAsync(token);
    }

    private static bool IsUnconsumedFollowUpResult(IncomingCoreCall call) =>
        call.Kind is CoreCallKind.Reconciliation or CoreCallKind.Reversal
        && call.Completion is not null
        && !call.Consumed;

    private static ReversalDelivery ReversalOutcome(CoreCallCompletion completion) => completion.Response switch
    {
        null => ReversalDelivery.Uncertain,
        { StatusCode: >= 200 and < 300 } => ReversalDelivery.Accepted,
        _ => ReversalDelivery.Unsuccessful
    };
}
