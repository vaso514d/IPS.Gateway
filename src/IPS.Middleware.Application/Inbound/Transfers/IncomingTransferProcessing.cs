using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Reconciliation;
using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Transfers;

// Hands a registered transfer to the core system and settles it with the core's answer. The first call submits; after an
// unanswered one the core is asked, and a core that never saw the transfer receives the same request again. Every call
// is safe to repeat because the EndToEndId is the idempotency key, so a crashed owner is simply replaced.
public sealed class IncomingTransferProcessing(
    IIncomingTransferRepository transfers,
    IUnitOfWork unitOfWork,
    IIncomingTransferCoreClient core,
    IIncomingTransferReplyInterpreter replies,
    IncomingReconciliationOptions options,
    TimeProvider timeProvider)
{
    private const string WindowExpired = "The core system did not settle the transfer within the fixed reconciliation window.";
    private const int NotFound = 404;

    private DateTimeOffset Now => timeProvider.GetUtcNow();

    public Task<IReadOnlyList<Guid>> DiscoverAsync(CancellationToken token) => transfers.FindDueAsync(Now, options.DiscoveryBatch, token);

    public async Task ProcessAsync(Guid transferId, CancellationToken token)
    {
        var snapshot = await transfers.ReadAsync(transferId, token);
        if (snapshot is null)
        {
            return;
        }

        try
        {
            var claim = await transfers.StageClaimAsync(transferId, Now, options.Ownership, token);
            if (claim is null)
            {
                return;
            }

            await unitOfWork.SaveAsync(token);
            await ContinueAsync(snapshot, claim, token);
        }
        catch (PersistenceConcurrencyException)
        {
            // Discard this scope; a competing owner continues from what it committed.
        }
    }

    private async Task ContinueAsync(IncomingTransferSnapshot snapshot, IncomingTransferClaim claim, CancellationToken token)
    {
        var transfer = snapshot.Transfer;
        if (transfer.IsFinal)
        {
            await ReleaseAsync(claim, null, token);
            return;
        }

        if (Now >= snapshot.DeadlineUtc)
        {
            transfer.RequireManualReview(WindowExpired, Now);
            await ReleaseAsync(claim, null, token);
            return;
        }

        var submitting = transfer.CoreStatus == CoreOutcome.NotSubmitted;
        if (submitting)
        {
            transfer.BeginSubmission(Now);
        }
        else
        {
            transfer.BeginQuery();
        }

        // The attempt is committed before the call, so a crash leaves a trace and the next owner asks the core.
        await unitOfWork.SaveAsync(token);
        token.ThrowIfCancellationRequested();

        var remaining = snapshot.DeadlineUtc - Now;
        var budget = remaining < options.CallTimeout ? remaining : options.CallTimeout;
        var participant = transfer.ParticipantBic;
        var completion = await CoreCallExecution.ExecuteAsync(
            callToken => submitting
                ? core.SubmitAsync(participant, snapshot.Content, callToken)
                : core.QueryAsync(participant, transfer.EndToEndId, callToken),
            budget,
            timeProvider,
            token);

        var result = replies.Interpret(completion, snapshot.Content);
        if (!submitting && result.Status == CoreOutcome.Unknown && completion.Response?.StatusCode == NotFound)
        {
            transfer.RequireResubmission(Now);
            await ReleaseAsync(claim, Now, token);
            return;
        }

        transfer.RecordCoreResult(result, Now);
        await ReleaseAsync(claim, result.Status == CoreOutcome.Unknown ? NextAttempt(transfer, snapshot.DeadlineUtc) : null, token);
    }

    // Configured delays apply to the first attempts, then the steady interval; never later than the window end.
    private DateTimeOffset NextAttempt(IncomingFiTransfer transfer, DateTimeOffset deadline)
    {
        var next = Now + options.RetryDelay(transfer.Attempts);
        return next < deadline ? next : deadline;
    }

    private async Task ReleaseAsync(IncomingTransferClaim claim, DateTimeOffset? nextActionAtUtc, CancellationToken token)
    {
        using var persistence = new CancellationTokenSource(options.PersistenceBudget, timeProvider);
        if (!await transfers.StageReleaseAsync(claim, Now, nextActionAtUtc, persistence.Token))
        {
            throw new PersistenceConcurrencyException("Transfer ownership expired.");
        }

        await unitOfWork.SaveAsync(persistence.Token);
    }
}
