using IPS.Middleware.Api.Diagnostics;
using IPS.Middleware.Application.Payments.Execution;
using IPS.Middleware.Infrastructure.Diagnostics;
using IPS.Middleware.Infrastructure.Inbound.Transport;
using IPS.Middleware.Infrastructure.Inbound.Workers;
using IPS.Middleware.Infrastructure.Payments.Transport;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IPS.Middleware.Api.Configuration;

internal static class DiagnosticsConfiguration
{
    internal const string ReadyTag = "ready";
    internal const string ReadyPath = "/health/ready";

    internal static IServiceCollection AddDiagnosticsConfiguration(this IServiceCollection services)
    {
        services.AddSingleton(ReadSettings);
        services.AddSingleton(ReadDatabaseUse);
        services.AddSingleton<ICertificateInventory, EnabledCertificateInventory>();
        services.AddHostedService<BacklogSnapshotService>();
        // Singletons, so a check remembers the problem it last logged.
        services.AddSingleton<DatabaseHealthCheck>();
        services.AddSingleton<WorkerHealthCheck>();
        services.AddSingleton<CertificateHealthCheck>();
        services.AddHealthChecks()
            .Add(Registration<DatabaseHealthCheck>("database"))
            .Add(Registration<WorkerHealthCheck>("workers"))
            .Add(Registration<CertificateHealthCheck>("certificates"));
        return services;
    }

    private static HealthCheckRegistration Registration<T>(string name) where T : class, IHealthCheck =>
        new(name, services => services.GetRequiredService<T>(), failureStatus: null, tags: [ReadyTag]);

    internal static void ValidateDiagnostics(this IServiceProvider services) => _ = services.GetRequiredService<DiagnosticsSettings>();

    private static DiagnosticsSettings ReadSettings(IServiceProvider services)
    {
        var settings = services.ReadSection<DiagnosticsSettings>("Diagnostics") ?? new DiagnosticsSettings();
        settings.Validate();
        return settings;
    }

    private static DatabaseUse ReadDatabaseUse(IServiceProvider services) => new(
        services.GetRequiredService<OutgoingTransportSettings>().Enabled
        || services.GetRequiredService<IncomingTransportSettings>().Enabled
        || services.GetRequiredService<OutgoingExecutionOptions>().Enabled
        || services.GetRequiredService<IncomingWorkerOptions>().Enabled);
}
