using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IPS.Middleware.Performance;

// The AppHost's stack for one run (SQL Server container, simulators, API instances as projects) with the chosen timings, and
// plain HTTP clients: no retry or resilience handler, so every request the generator makes reaches the service exactly once.
internal sealed class LoadStack : IAsyncDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(5);
    private readonly DistributedApplication _application;

    private LoadStack(DistributedApplication application, int instances)
    {
        _application = application;
        var endpoints = Enumerable.Range(1, instances)
            .Select(number => application.GetEndpoint(Name(number), "http"))
            .ToArray();
        // The API answers within its HttpWait (30 s shipped, 21 s in the test timings); a request still open after a minute counts as
        // unanswered.
        Instances = endpoints
            .Select(endpoint => new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromMinutes(1) })
            .ToArray();
        Probes = endpoints
            .Select(endpoint => new HttpClient { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(10) })
            .ToArray();
        // The control API is on the simulators' plain endpoint.
        Simulators = new HttpClient { BaseAddress = application.GetEndpoint("simulators", "http"), Timeout = TimeSpan.FromMinutes(5) };
    }

    internal IReadOnlyList<HttpClient> Instances { get; }
    internal IReadOnlyList<HttpClient> Probes { get; }
    internal HttpClient Simulators { get; }

    internal static async Task<LoadStack> StartAsync(int instances, int concurrency, bool signing, string timings, CancellationToken cancellationToken)
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.IPS_Middleware_AppHost>(
        [
            $"--Middleware:Instances={instances}",
            $"--Middleware:Concurrency={concurrency}",
            $"--Middleware:Signing={signing}",
            $"--Middleware:Timings={timings}"
        ], cancellationToken);
        // Writing every resource's log lines would cost the machine time under load; the orchestrator's warnings and errors still
        // show, and the instances' own logs are counted by InstanceLogs. The AppHost's health checks fail while the containers
        // start, and readiness during the run is sampled separately.
        builder.Services.AddLogging(logging => logging
            .SetMinimumLevel(LogLevel.Warning)
            .AddFilter("Microsoft.Extensions.Diagnostics.HealthChecks", LogLevel.None));
        var application = await builder.BuildAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(StartTimeout);
            await application.StartAsync(timeout.Token);
            await application.ResourceNotifications.WaitForResourceHealthyAsync("simulators", timeout.Token);
            for (var number = 1; number <= instances; number++)
            {
                await application.ResourceNotifications.WaitForResourceHealthyAsync(Name(number), timeout.Token);
            }

            return new LoadStack(application, instances);
        }
        catch
        {
            // The caller never receives a stack to dispose, so a failed start stops the containers here.
            await application.DisposeAsync();
            throw;
        }
    }

    internal async Task<string> ConnectionStringAsync(CancellationToken cancellationToken) =>
        await _application.GetConnectionStringAsync("Middleware", cancellationToken)
            ?? throw new InvalidOperationException("The database has no connection string.");

    // What the instance writes from now on. Console logs are kept under the orchestrator's id of the running instance, not the name.
    internal Task<InstanceLogs> LogsOfAsync(int number, CancellationToken cancellationToken) =>
        InstanceLogs.StartAsync(_application.Services.GetRequiredService<ResourceLoggerService>(), State(number).ResourceId, number, cancellationToken);

    // The environment the AppHost gave the instance, as the orchestrator reports it.
    internal IReadOnlyList<EnvironmentVariableSnapshot> EnvironmentOf(int number) => State(number).Snapshot.EnvironmentVariables
        .Where(variable => variable.IsFromSpec)
        .ToArray();

    public async ValueTask DisposeAsync()
    {
        foreach (var client in Instances.Concat(Probes))
        {
            client.Dispose();
        }

        Simulators.Dispose();
        await _application.DisposeAsync();
    }

    private ResourceEvent State(int number) => _application.ResourceNotifications.TryGetCurrentState(Name(number), out var resource)
        ? resource
        : throw new InvalidOperationException($"{Name(number)} has no state.");

    private static string Name(int number) => $"middleware-{number}";
}
