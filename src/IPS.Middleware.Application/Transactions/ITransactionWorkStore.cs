using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Transactions;

/// <summary>Durable scheduling and ownership; decisions run once and must not perform external side effects.</summary>
public interface ITransactionWorkStore
{
    Task<IReadOnlyList<Guid>> FindDueAsync(
        TransactionStatus status, DateTimeOffset now, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> FindExpiredAsync(
        DateTimeOffset now, int take, CancellationToken cancellationToken);

    Task<TransactionClaim?> TryClaimAsync(
        Guid id, DateTimeOffset now, TimeSpan duration,
        Func<PaymentTransaction, bool> start, CancellationToken cancellationToken);

    /// <summary>A successful decision releases ownership; unchanged decisions retain it. Expiry is evaluated at the supplied operation time; expired/stale owners return Conflict.</summary>
    Task<TransactionUpdateResult> TryCompleteAsync(
        TransactionClaim claim, DateTimeOffset now, Func<PaymentTransaction, bool> change,
        DateTimeOffset? nextActionAtUtc, CancellationToken cancellationToken);

    Task<TransactionUpdateResult> TryRecoverAsync(
        Guid id, DateTimeOffset now, Func<PaymentTransaction, bool> recover, CancellationToken cancellationToken);
}

public sealed record TransactionClaim(Guid TransactionId, Guid Token, DateTimeOffset ExpiresAtUtc);
