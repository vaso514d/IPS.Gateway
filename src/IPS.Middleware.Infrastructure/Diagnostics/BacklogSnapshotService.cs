using IPS.Middleware.Application.Diagnostics;
using IPS.Middleware.Infrastructure.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IPS.Middleware.Infrastructure.Diagnostics;

// Refreshes the backlog gauges on an interval instead of reading SQL on every scrape. A failed read keeps the previous snapshot.
public sealed class BacklogSnapshotService(
    IServiceScopeFactory scopes,
    DiagnosticsSettings settings,
    DatabaseUse database,
    TimeProvider time,
    ILogger<BacklogSnapshotService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.BacklogSnapshot || !database.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var reader = new BacklogReader(scope.ServiceProvider.GetRequiredService<TransactionDbContext>(), time);
                PaymentMetrics.PublishBacklog(await reader.ReadAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "The backlog snapshot failed; the previous one stays until the next interval");
                PaymentMetrics.ErrorLogged(nameof(BacklogSnapshotService));
            }

            await Task.Delay(settings.SnapshotInterval, time, stoppingToken);
        }
    }
}
