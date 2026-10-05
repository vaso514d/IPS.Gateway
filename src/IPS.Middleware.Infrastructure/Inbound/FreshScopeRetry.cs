using IPS.Middleware.Application.Abstractions.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.Middleware.Infrastructure.Inbound;

internal static class FreshScopeRetry
{
    // Runs each attempt in a new scope. A concurrent writer's uniqueness or rowversion win fails the scope, which is
    // discarded, never replayed; the next attempt reads the committed state.
    internal static async Task<TResult> RetryAsync<TService, TResult>(
        this IServiceScopeFactory scopes,
        Func<TService, Task<TResult>> attempt,
        int maxAttempts,
        CancellationToken cancellationToken) where TService : notnull
    {
        for (var number = 1; ; number++)
        {
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                return await attempt(scope.ServiceProvider.GetRequiredService<TService>());
            }
            catch (Exception error) when (number < maxAttempts && error is UniqueConstraintException or PersistenceConcurrencyException)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}
