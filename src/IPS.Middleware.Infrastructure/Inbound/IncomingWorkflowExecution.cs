using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound.Composition;
using IPS.Middleware.Application.Inbound.Processing;
using IPS.Middleware.Application.Inbound.Receipts;
using IPS.Middleware.Application.Inbound.Replies;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.Middleware.Infrastructure.Inbound;

public sealed class IncomingWorkflowExecution(IServiceScopeFactory scopes, InboundSchedulingOptions options,
    InboundReplyChannel channel, TimeProvider time) : IIncomingWorkflowExecution
{
    public async Task<IncomingReceiptState?> ReadAsync(Guid journalId, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IIncomingCompositionRepository>().ReadAsync(journalId, token);
    }

    public async Task<IncomingCompositionResult> PrepareAsync(Guid journalId, CancellationToken token)
    {
        InboundClaim? claim;
        await using (var scope = scopes.CreateAsyncScope())
            claim = await scope.ServiceProvider.GetRequiredService<InboundWork>().AcquireAsync(journalId, options.ClaimDuration, token);
        if (claim is null) return new(IncomingCompositionStatus.OwnershipLost);
        try
        {
            return await scopes.RetryAsync<IncomingReceiptPreparation, IncomingCompositionResult>(
                preparation => preparation.PrepareAsync(claim, token), options.RegistrationMaxAttempts, token);
        }
        catch (PersistenceConcurrencyException) { return new(IncomingCompositionStatus.OwnershipLost); }
    }

    public async Task<IncomingProcessingResult?> ProcessPaymentAsync(Guid paymentId, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IncomingPacs008Processing>().ProcessAsync(paymentId, token);
    }

    public async Task<bool> MakeFirstReplyReadyAsync(Guid journalId, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        try
        {
            if (!await scope.ServiceProvider.GetRequiredService<IIncomingCompositionRepository>()
                .StageFirstReplyReadyAsync(journalId, time.GetUtcNow(), token)) return false;
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveAsync(token);
            return true;
        }
        catch (PersistenceConcurrencyException) { return false; }
    }

    public async Task DeliverReplyAsync(Guid journalId, CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IncomingReplyProcessing>().ProcessAsync(journalId, token);
    }

    public bool TryNotifyReply(Guid journalId) => channel.TryNotify(journalId);
}
