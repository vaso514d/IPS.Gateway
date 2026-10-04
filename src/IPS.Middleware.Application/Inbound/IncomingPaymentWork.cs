using IPS.Middleware.Application.Abstractions.Inbound;
using IPS.Middleware.Application.Abstractions.Persistence;

namespace IPS.Middleware.Application.Inbound;

/// <summary>Payment ownership, independent of receipt ownership: only the owner may process the payment.</summary>
public sealed class IncomingPaymentWork(IIncomingPaymentWorkRepository repository, IUnitOfWork unitOfWork, TimeProvider timeProvider)
{
    /// <summary>Returns ownership only after it commits. Expiry permits reacquisition, never repeating a remote call.</summary>
    public async Task<IncomingPaymentClaim?> AcquireAsync(Guid paymentId, TimeSpan duration, CancellationToken cancellationToken)
    {
        var claim = await repository.StageClaimAsync(paymentId, timeProvider.GetUtcNow(), duration, cancellationToken);
        return claim is not null && await TryCommitAsync(cancellationToken) ? claim : null;
    }

    public async Task<bool> ReleaseAsync(IncomingPaymentClaim claim, DateTimeOffset nextActionAtUtc, CancellationToken cancellationToken) =>
        await repository.StageReleaseAsync(claim, timeProvider.GetUtcNow(), nextActionAtUtc, cancellationToken) &&
        await TryCommitAsync(cancellationToken);

    // A competing owner changed the payment first; the caller discards this scope.
    private async Task<bool> TryCommitAsync(CancellationToken cancellationToken)
    {
        try { await unitOfWork.SaveAsync(cancellationToken); return true; }
        catch (PersistenceConcurrencyException) { return false; }
    }
}

public sealed record IncomingPaymentClaim(Guid PaymentId, Guid Token, DateTimeOffset ExpiresAtUtc);
