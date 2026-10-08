using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace IPS.Middleware.AspireTests;

// The whole deployment (SQL Server container, simulators and the API instances) started the way the AppHost declares it.
internal sealed class Stack : IAsyncDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(5);
    private readonly DistributedApplication _application;
    private readonly HttpClient _simulators;
    private readonly List<HttpClient> _clients = [];

    private Stack(DistributedApplication application, int instances)
    {
        _application = application;
        Instances = instances;
        // The control API is on the plain endpoint; the TLS endpoint carries only what the service itself calls.
        _simulators = new HttpClient { BaseAddress = application.GetEndpoint("simulators", "http") };
    }

    internal int Instances { get; }

    internal static async Task<Stack> StartAsync(int instances = 1, bool container = false)
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.IPS_Middleware_AppHost>(
        [
            $"--Middleware:Instances={instances}",
            $"--Middleware:Container={container}"
        ]);
        var application = await builder.BuildAsync();
        try
        {
            using var timeout = new CancellationTokenSource(StartTimeout);
            await application.StartAsync(timeout.Token);
            await application.ResourceNotifications.WaitForResourceHealthyAsync("simulators", timeout.Token);
            for (var number = 1; number <= instances; number++)
            {
                await application.ResourceNotifications.WaitForResourceHealthyAsync($"middleware-{number}", timeout.Token);
            }

            return new Stack(application, instances);
        }
        catch
        {
            // The caller never receives a Stack to dispose, so a failed start stops the containers and deletes the run's files here.
            await application.DisposeAsync();
            throw;
        }
    }

    internal HttpClient Api(int number = 1)
    {
        var client = _application.CreateHttpClient($"middleware-{number}", "http");
        _clients.Add(client);
        return client;
    }

    // What the simulated IPS, CBS and Proxy Solution have received since the last reset.
    internal async Task<Received> ReceivedAsync()
    {
        using var document = JsonDocument.Parse(await _simulators.GetStringAsync("/_sim/received"));
        var root = document.RootElement;
        return new Received(
            root.GetProperty("messages").EnumerateArray().Select(message => new ReceivedMessage(
                message.GetProperty("xml").GetString()!,
                message.GetProperty("possibleDuplicate").GetBoolean(),
                message.GetProperty("version").GetString())).ToArray(),
            root.GetProperty("callbacks").EnumerateArray().Select(callback => callback.GetString()!).ToArray(),
            root.GetProperty("proxyCalls").EnumerateArray().Select(call => call.GetProperty("operation").GetString()!).ToArray());
    }

    // Polls what the simulators received until the condition holds, so a test waits for the asynchronous callback it expects.
    internal async Task<Received> WaitForAsync(Func<Received, bool> condition, TimeSpan? timeout = null)
    {
        using var budget = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(60));
        while (true)
        {
            var received = await ReceivedAsync();
            if (condition(received))
            {
                return received;
            }

            await Task.Delay(100, budget.Token);
        }
    }

    // The same, then observes for a short settle that nothing more arrives, so "exactly once" is not just "at least once".
    internal async Task<Received> WaitForSettledAsync(Func<Received, bool> condition, TimeSpan? timeout = null)
    {
        await WaitForAsync(condition, timeout);
        await Task.Delay(TimeSpan.FromSeconds(1));
        var settled = await ReceivedAsync();
        return condition(settled) ? settled : throw new InvalidOperationException("The simulators received more after the expected result.");
    }

    internal async Task BehaveAsync(object behaviour)
    {
        using var response = await _simulators.PostAsJsonAsync("/_sim/behaviour", behaviour, JsonSerializerOptions.Web);
        response.EnsureSuccessStatusCode();
    }

    internal async Task ReleaseAsync() => (await _simulators.PostAsync("/_sim/release", null)).EnsureSuccessStatusCode();

    internal async Task ResetAsync() => (await _simulators.PostAsync("/_sim/reset", null)).EnsureSuccessStatusCode();

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        _simulators.Dispose();
        await _application.DisposeAsync();
    }

    internal sealed record ReceivedMessage(string Xml, bool PossibleDuplicate, string? Version);

    internal sealed record Received(IReadOnlyList<ReceivedMessage> Messages, IReadOnlyList<string> Callbacks, IReadOnlyList<string> ProxyCalls);
}
