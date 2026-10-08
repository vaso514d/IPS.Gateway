namespace IPS.Middleware.Application.Abstractions.Persistence;

public interface IUnitOfWork
{
    Task<int> SaveAsync(CancellationToken cancellationToken = default);
}
