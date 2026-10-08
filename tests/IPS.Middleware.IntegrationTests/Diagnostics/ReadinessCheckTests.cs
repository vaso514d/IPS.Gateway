using IPS.Middleware.Api.Diagnostics;
using IPS.Middleware.Infrastructure.Diagnostics;
using IPS.Middleware.Infrastructure.Hosting;
using IPS.Middleware.Infrastructure.Transactions;
using IPS.Middleware.Infrastructure.Transport;
using IPS.Middleware.IntegrationTests.Payments;
using IPS.Middleware.IntegrationTests.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IPS.Middleware.IntegrationTests.Diagnostics;

public sealed class ReadinessCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_database_check_is_healthy_when_it_connects_and_every_migration_is_applied()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        using var services = Services(database.Context().Database.GetConnectionString()!);

        var result = await Check(services, use: true);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task The_database_check_is_unhealthy_for_a_pending_migration()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using (var context = database.Context())
        {
            var latest = (await context.Database.GetAppliedMigrationsAsync()).Last();
            await context.Database.ExecuteSqlRawAsync("DELETE FROM [__EFMigrationsHistory] WHERE [MigrationId] = {0}", latest);
        }

        using var services = Services(database.Context().Database.GetConnectionString()!);

        Assert.Equal(HealthStatus.Unhealthy, (await Check(services, use: true)).Status);
    }

    [Fact]
    public async Task The_database_check_is_unhealthy_for_a_migration_this_build_does_not_know()
    {
        await using var database = await SqlTestDatabase.CreateAsync();
        await using (var context = database.Context())
        {
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES ('29990101000000_FromTheFuture', '10.0.0')");
        }

        using var services = Services(database.Context().Database.GetConnectionString()!);

        Assert.Equal(HealthStatus.Unhealthy, (await Check(services, use: true)).Status);
    }

    [Fact]
    public async Task The_database_check_is_unhealthy_when_the_server_cannot_be_reached_within_its_timeout()
    {
        using var services = Services("Server=127.0.0.1,1;Database=Missing;User Id=u;Password=p;Connect Timeout=1;TrustServerCertificate=true;Encrypt=false");

        var result = await Check(services, use: true);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task The_database_check_does_not_touch_the_database_when_no_enabled_feature_uses_it()
    {
        using var services = Services("Server=127.0.0.1,1;Database=Missing;Connect Timeout=1");

        Assert.Equal(HealthStatus.Healthy, (await Check(services, use: false)).Status);
    }

    [Fact]
    public void A_disabled_worker_is_healthy_and_one_that_has_not_started_is_not()
    {
        using var disabled = new TestWorker(enabled: false);
        using var notStarted = new TestWorker(enabled: true);

        Assert.Equal(WorkerState.Disabled, disabled.Health(Now, 3, TimeSpan.Zero).State);
        Assert.True(disabled.Health(Now, 3, TimeSpan.Zero).IsHealthy);
        Assert.Equal(WorkerState.NotStarted, notStarted.Health(Now, 3, TimeSpan.Zero).State);
    }

    [Fact]
    public async Task A_running_worker_is_healthy_until_a_loop_misses_its_period_by_the_stall_factor()
    {
        var clock = new TestClock(Now);
        using var worker = new TestWorker(enabled: true, clock);
        await worker.StartAsync(default);
        await worker.Started;

        Assert.Equal(WorkerState.Running, worker.Health(clock.Now, 3, TimeSpan.Zero).State);
        clock.Now += TimeSpan.FromSeconds(29);
        Assert.Equal(WorkerState.Running, worker.Health(clock.Now, 3, TimeSpan.Zero).State);
        clock.Now += TimeSpan.FromSeconds(2);
        var stalled = worker.Health(clock.Now, 3, TimeSpan.Zero);

        Assert.Equal(WorkerState.Stalled, stalled.State);
        Assert.Contains("loop", stalled.Detail, StringComparison.Ordinal);
        worker.Release();
        await worker.StopAsync(default);
    }

    [Fact]
    public async Task The_pass_allowance_keeps_a_busy_loop_with_a_short_period_from_reading_as_stalled()
    {
        var clock = new TestClock(Now);
        using var worker = new TestWorker(enabled: true, clock);
        await worker.StartAsync(default);
        await worker.Started;

        clock.Now += TimeSpan.FromSeconds(35);

        Assert.Equal(WorkerState.Stalled, worker.Health(clock.Now, 3, TimeSpan.Zero).State);
        Assert.Equal(WorkerState.Running, worker.Health(clock.Now, 3, TimeSpan.FromSeconds(10)).State);
        Assert.Equal(WorkerState.Stalled, worker.Health(clock.Now.AddSeconds(6), 3, TimeSpan.FromSeconds(10)).State);
        worker.Release();
        await worker.StopAsync(default);
    }

    [Fact]
    public async Task A_worker_that_beats_again_recovers_from_a_stall()
    {
        var clock = new TestClock(Now);
        using var worker = new TestWorker(enabled: true, clock);
        await worker.StartAsync(default);
        await worker.Started;
        clock.Now += TimeSpan.FromMinutes(5);
        Assert.Equal(WorkerState.Stalled, worker.Health(clock.Now, 3, TimeSpan.Zero).State);

        worker.BeatNow();

        Assert.Equal(WorkerState.Running, worker.Health(clock.Now, 3, TimeSpan.Zero).State);
        worker.Release();
        await worker.StopAsync(default);
    }

    [Fact]
    public async Task A_worker_that_is_stopping_is_draining_and_one_that_has_stopped_is_not_ready()
    {
        using var worker = new TestWorker(enabled: true);
        await worker.StartAsync(default);
        await worker.Started;

        var stopping = worker.StopAsync(default);
        var draining = worker.Health(Now, 3, TimeSpan.Zero);
        worker.Release();
        await stopping;

        Assert.Equal(WorkerState.Draining, draining.State);
        Assert.False(draining.IsHealthy);
    }

    [Fact]
    public async Task A_worker_whose_loop_ended_or_failed_is_stopped_or_faulted()
    {
        using var ended = new TestWorker(enabled: true, completes: true);
        using var failed = new TestWorker(enabled: true, fails: true);
        await ended.StartAsync(default);
        await failed.StartAsync(default);
        await Task.WhenAny(ended.ExecuteTask!, Task.Delay(TimeSpan.FromSeconds(10)));
        await Task.WhenAny(failed.ExecuteTask!, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Equal(WorkerState.Stopped, ended.Health(Now, 3, TimeSpan.Zero).State);
        Assert.Equal(WorkerState.Faulted, failed.Health(Now, 3, TimeSpan.Zero).State);
    }

    [Fact]
    public async Task The_worker_check_reports_unhealthy_when_any_supervised_service_is_not_healthy_and_ignores_other_services()
    {
        using var healthy = new TestWorker(enabled: false);
        using var notStarted = new TestWorker(enabled: true);
        var settings = new DiagnosticsSettings();

        var ok = await new WorkerHealthCheck([healthy, new OtherService()], settings, new TestClock(Now), NullLogger<WorkerHealthCheck>.Instance)
            .CheckHealthAsync(new());
        var bad = await new WorkerHealthCheck([healthy, notStarted], settings, new TestClock(Now), NullLogger<WorkerHealthCheck>.Instance)
            .CheckHealthAsync(new());

        Assert.Equal(HealthStatus.Healthy, ok.Status);
        Assert.Equal(HealthStatus.Unhealthy, bad.Status);
    }

    [Theory]
    [InlineData(90, HealthStatus.Healthy)]
    [InlineData(30, HealthStatus.Degraded)]
    [InlineData(5, HealthStatus.Degraded)]
    [InlineData(0, HealthStatus.Unhealthy)]
    [InlineData(-3, HealthStatus.Unhealthy)]
    public async Task The_certificate_check_follows_the_days_left_of_the_nearest_expiry(int daysLeft, HealthStatus expected)
    {
        var inventory = new FixedInventory(
            new CertificateExpiry("outgoing", "CN=signing", Now.AddDays(daysLeft), Now.AddYears(-1)),
            new CertificateExpiry("outgoing", "CN=other", Now.AddDays(365), Now.AddYears(-1)));

        var result = await new CertificateHealthCheck(inventory, new DiagnosticsSettings(), new TestClock(Now), NullLogger<CertificateHealthCheck>.Instance)
            .CheckHealthAsync(new());

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task The_certificate_check_is_healthy_with_nothing_loaded_and_its_result_carries_no_certificate_detail()
    {
        var empty = await new CertificateHealthCheck(new FixedInventory(), new DiagnosticsSettings(), new TestClock(Now), NullLogger<CertificateHealthCheck>.Instance)
            .CheckHealthAsync(new());
        var expired = await new CertificateHealthCheck(new FixedInventory(new CertificateExpiry("proxy", "CN=secret-subject", Now.AddDays(-1), Now.AddYears(-1))),
                new DiagnosticsSettings(), new TestClock(Now), NullLogger<CertificateHealthCheck>.Instance)
            .CheckHealthAsync(new());

        Assert.Equal(HealthStatus.Healthy, empty.Status);
        Assert.DoesNotContain("secret-subject", expired.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_certificate_that_is_not_valid_yet_is_named_as_information_without_changing_the_status()
    {
        var start = Now.AddDays(3);
        var inventory = new FixedInventory(
            new CertificateExpiry("incoming", "CN=Next IPS", Now.AddYears(2), start),
            new CertificateExpiry("incoming", "CN=Current IPS", Now.AddDays(365), Now.AddYears(-1)));
        var log = new ScopeLog();

        var check = new CertificateHealthCheck(inventory, new DiagnosticsSettings(), new TestClock(Now), new TypedLog<CertificateHealthCheck>(log));

        var result = await check.CheckHealthAsync(new());
        var repeated = await check.CheckHealthAsync(new());

        Assert.Equal((HealthStatus.Healthy, HealthStatus.Healthy), (result.Status, repeated.Status));
        Assert.DoesNotContain("Next IPS", result.Description, StringComparison.Ordinal);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains($"incoming certificate (CN=Next IPS) is not valid until {start:O}", entry.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Settings_reject_non_positive_intervals_and_a_stall_factor_below_one(double factor, bool valid)
    {
        var settings = new DiagnosticsSettings { WorkerStallFactor = factor };

        if (valid)
        {
            settings.Validate();
        }
        else
        {
            Assert.Throws<InvalidOperationException>(settings.Validate);
        }

        Assert.Throws<InvalidOperationException>(new DiagnosticsSettings { SnapshotInterval = TimeSpan.Zero }.Validate);
        Assert.Throws<InvalidOperationException>(new DiagnosticsSettings { CertificateWarning = TimeSpan.Zero }.Validate);
        Assert.Throws<InvalidOperationException>(new DiagnosticsSettings { DatabaseTimeout = TimeSpan.FromMinutes(6) }.Validate);
        Assert.Throws<InvalidOperationException>(new DiagnosticsSettings { WorkerStallFactor = double.NaN }.Validate);
        Assert.Throws<InvalidOperationException>(new DiagnosticsSettings { WorkerPassAllowance = TimeSpan.FromSeconds(-1) }.Validate);
        Assert.Throws<InvalidOperationException>(new DiagnosticsSettings { SnapshotInterval = TimeSpan.FromDays(2) }.Validate);
    }

    private static ServiceProvider Services(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TransactionDbContext>(options => options.UseSqlServer(connectionString));
        return services.BuildServiceProvider();
    }

    private static Task<HealthCheckResult> Check(ServiceProvider services, bool use) =>
        new DatabaseHealthCheck(
                services.GetRequiredService<IServiceScopeFactory>(),
                new DatabaseUse(use),
                new DiagnosticsSettings { DatabaseTimeout = TimeSpan.FromSeconds(3) },
                NullLogger<DatabaseHealthCheck>.Instance)
            .CheckHealthAsync(new());

    private sealed class OtherService : Microsoft.Extensions.Hosting.IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedInventory(params CertificateExpiry[] expiries) : ICertificateInventory
    {
        public IEnumerable<ICertificateExpirySource> Sources() => [new Source(expiries)];

        private sealed class Source(CertificateExpiry[] expiries) : ICertificateExpirySource
        {
            public IReadOnlyList<CertificateExpiry> Expiries() => expiries;
        }
    }

    // Lets a check that needs a typed logger write into a ScopeLog.
    internal sealed class TypedLog<T>(ScopeLog log) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => log.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => log.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            log.Log(logLevel, eventId, state, exception, formatter);
    }

    private sealed class TestWorker(bool enabled, TestClock? clock = null, bool completes = false, bool fails = false)
        : SupervisedBackgroundService(enabled, TimeSpan.FromSeconds(5), clock ?? new TestClock(Now), NullLogger.Instance)
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        public void BeatNow() => Beat("loop", TimeSpan.FromSeconds(10));

        protected override async Task RunAsync(CancellationToken stop, CancellationToken work)
        {
            if (fails)
            {
                throw new InvalidOperationException("The loop failed.");
            }

            if (completes)
            {
                return;
            }

            Beat("loop", TimeSpan.FromSeconds(10));
            _started.TrySetResult();
            await _release.Task.WaitAsync(stop);
        }
    }
}
