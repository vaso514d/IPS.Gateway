using IPS.Middleware.Infrastructure.Diagnostics;
using IPS.Middleware.Infrastructure.Hosting;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Payments.Transport;
using IPS.Middleware.Infrastructure.Proxy;
using IPS.Middleware.Infrastructure.Transactions;
using IPS.Middleware.Infrastructure.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IPS.Middleware.Api.Diagnostics;

// Readiness answers whether this instance can take traffic. The status is the only thing the endpoint returns; what failed
// is logged, so nothing about configuration or certificates is exposed on an unauthenticated route.
internal sealed class DatabaseHealthCheck(
    IServiceScopeFactory scopes,
    DatabaseUse use,
    DiagnosticsSettings settings,
    ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!use.Enabled)
        {
            return HealthCheckResult.Healthy("No enabled feature uses the database.");
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(settings.DatabaseTimeout);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<TransactionDbContext>().Database;
            if (!await database.CanConnectAsync(budget.Token))
            {
                logger.LogWarning("Readiness: the database cannot be reached");
                return HealthCheckResult.Unhealthy("The database cannot be reached.");
            }

            var known = database.GetMigrations().ToHashSet(StringComparer.Ordinal);
            var applied = (await database.GetAppliedMigrationsAsync(budget.Token)).ToHashSet(StringComparer.Ordinal);
            if (!known.SetEquals(applied))
            {
                logger.LogWarning(
                    "Readiness: the database migrations do not match the model (pending: {Pending}; unknown: {Unknown})",
                    string.Join(", ", known.Except(applied)),
                    string.Join(", ", applied.Except(known)));
                return HealthCheckResult.Unhealthy("The database migrations do not match the model.");
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(error, "Readiness: the database check failed or timed out");
            return HealthCheckResult.Unhealthy("The database check failed.");
        }
    }
}

internal sealed class WorkerHealthCheck(
    IEnumerable<IHostedService> services,
    DiagnosticsSettings settings,
    TimeProvider time,
    ILogger<WorkerHealthCheck> logger) : IHealthCheck
{
    private string _lastProblem = "";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var unhealthy = services
            .OfType<SupervisedBackgroundService>()
            .Select(service => service.Health(time.GetUtcNow(), settings.WorkerStallFactor, settings.WorkerPassAllowance))
            .Where(health => !health.IsHealthy)
            .ToArray();
        // A probe repeats every few seconds, so a problem is logged when it appears or changes, not on every probe.
        var problem = string.Join("; ", unhealthy.Select(health => $"{health.Name} is {health.State}: {health.Detail}"));
        if (problem != _lastProblem)
        {
            _lastProblem = problem;
            if (problem.Length != 0)
            {
                logger.LogWarning("Readiness: {Problem}", problem);
            }
        }

        return Task.FromResult(unhealthy.Length == 0 ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("A worker is not running."));
    }
}

internal sealed class CertificateHealthCheck(
    ICertificateInventory inventory,
    DiagnosticsSettings settings,
    TimeProvider time,
    ILogger<CertificateHealthCheck> logger) : IHealthCheck
{
    private string _lastProblem = "";
    private string _lastNotice = "";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var expiries = inventory.Sources().SelectMany(source => source.Expiries()).ToArray();
        var expired = expiries.Where(certificate => certificate.NotAfterUtc <= now).ToArray();
        var expiring = expiries.Where(certificate => certificate.NotAfterUtc > now && certificate.NotAfterUtc - now <= settings.CertificateWarning).ToArray();
        // Only IPS signature trust loads before its validity starts (012b): the next certificate, configured for rotation.
        var notYetValid = expiries.Where(certificate => certificate.NotBeforeUtc > now).ToArray();
        var problem = string.Join("; ",
            expired.Select(certificate => $"a {certificate.Source} certificate ({certificate.Subject}) expired at {certificate.NotAfterUtc:O}")
                .Concat(expiring.Select(certificate => $"a {certificate.Source} certificate ({certificate.Subject}) expires at {certificate.NotAfterUtc:O}")));
        var notice = string.Join("; ",
            notYetValid.Select(certificate => $"a {certificate.Source} certificate ({certificate.Subject}) is not valid until {certificate.NotBeforeUtc:O}"));
        if (problem != _lastProblem)
        {
            _lastProblem = problem;
            if (problem.Length != 0)
            {
                logger.LogWarning("Readiness: {Problem}", problem);
            }
        }

        if (notice != _lastNotice)
        {
            _lastNotice = notice;
            if (notice.Length != 0)
            {
                logger.LogInformation("Readiness: {Notice}", notice);
            }
        }

        if (expired.Length != 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("A certificate has expired."));
        }

        if (expiring.Length != 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded("A certificate expires soon."));
        }

        return Task.FromResult(HealthCheckResult.Healthy(notYetValid.Length != 0 ? "A signature trust certificate is not valid yet." : null));
    }

}

// The certificate owners readiness inspects.
internal interface ICertificateInventory
{
    IEnumerable<ICertificateExpirySource> Sources();
}

// Only the certificates of enabled transports are loaded, so only those are inspected.
internal sealed class EnabledCertificateInventory(
    IServiceProvider services,
    OutgoingTransportSettings outgoing,
    IncomingTransportSettings incoming,
    ProxySettings proxy) : ICertificateInventory
{
    public IEnumerable<ICertificateExpirySource> Sources()
    {
        if (outgoing.Enabled)
        {
            yield return services.GetRequiredService<OutgoingTransportCertificates>();
        }

        if (incoming.Enabled)
        {
            yield return services.GetRequiredService<IncomingTransportCertificates>();
        }

        if (proxy.Enabled)
        {
            yield return services.GetRequiredService<ProxyTransportCertificates>();
        }
    }
}
