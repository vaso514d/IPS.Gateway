namespace IPS.Middleware.Application.Abstractions.Persistence;

public interface IUnitOfWork
{
    /// <summary>Atomically save tracked changes and domain events. Discard the scope after a failed save.</summary>
    Task<int> SaveAsync(CancellationToken cancellationToken = default);
}
