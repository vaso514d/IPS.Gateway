using IPS.Middleware.Application.Abstractions.Persistence;
using IPS.Middleware.Application.Inbound;
using Microsoft.Extensions.DependencyInjection;

namespace IPS.Middleware.Infrastructure.Inbound;

/// <summary>Each registration attempt owns a fresh scope; failed units are never replayed.</summary>
public sealed class InboundReceiptRegistration(IServiceScopeFactory scopes, InboundProcessingChannel channel)
{
    private const int MaxAttempts = 8;

    public async Task<InboundRegistration> RegisterAsync(InboundReceipt receipt, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            try
            {
                var result = await scope.ServiceProvider.GetRequiredService<InboundReceiptIntake>().RegisterAsync(receipt, cancellationToken);
                // Notify only after a new pending receipt commits; anything missed is rediscovered from SQL.
                if (result is { Created: true, Status: InboundProcessingStatus.Pending } && !cancellationToken.IsCancellationRequested)
                    channel.TryNotify(result.JournalId);
                return result;
            }
            // A concurrent delivery of the same sequence won the insert or update; the next attempt sees it as a duplicate.
            catch (Exception error) when (attempt < MaxAttempts && error is UniqueConstraintException or PersistenceConcurrencyException)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}
