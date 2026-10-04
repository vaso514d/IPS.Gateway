using IPS.Middleware.Application.Abstractions.Inbound;
using IPS.Middleware.Application.Abstractions.Persistence;

namespace IPS.Middleware.Application.Inbound;

public sealed class InboundWork(IInboundWorkRepository repository, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    public async Task<InboundClaim?> AcquireAsync(Guid journalId, TimeSpan duration, CancellationToken cancellationToken)
    {
        var claim = await repository.StageClaimAsync(journalId, timeProvider.GetUtcNow(), duration, cancellationToken);
        return claim is not null && await TryCommitAsync(cancellationToken) ? claim : null;
    }

    public Task<bool> CompleteAsync(InboundClaim claim, CancellationToken cancellationToken) =>
        FinishAsync(claim, null, cancellationToken);

    public Task<bool> ReleaseAsync(InboundClaim claim, DateTimeOffset nextActionAtUtc, CancellationToken cancellationToken) =>
        FinishAsync(claim, nextActionAtUtc, cancellationToken);

    private async Task<bool> FinishAsync(InboundClaim claim, DateTimeOffset? nextActionAtUtc, CancellationToken cancellationToken) =>
        await repository.StageFinishAsync(claim, timeProvider.GetUtcNow(), nextActionAtUtc, cancellationToken) &&
        await TryCommitAsync(cancellationToken);

    // A competing owner or duplicate delivery changed the row first; the caller discards this scope.
    private async Task<bool> TryCommitAsync(CancellationToken cancellationToken)
    {
        try { await unitOfWork.SaveAsync(cancellationToken); return true; }
        catch (PersistenceConcurrencyException) { return false; }
    }
}
