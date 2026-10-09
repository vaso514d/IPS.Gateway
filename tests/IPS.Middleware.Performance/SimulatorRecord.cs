using System.Text.Json;

namespace IPS.Middleware.Performance;

internal sealed record IpsMessage(string Xml, bool PossibleDuplicate, DateTimeOffset ReceivedAtUtc);

internal sealed record CoreCallback(string Body, DateTimeOffset ReceivedAtUtc);

// What the simulators recorded over the run (/_sim/received): every message the simulated IPS received and every callback the
// simulated core received, each with its receive time on this machine's clock.
internal sealed record SimulatorRecord(IReadOnlyList<IpsMessage> Messages, IReadOnlyList<CoreCallback> Callbacks)
{
    internal static async Task<SimulatorRecord> ReadAsync(HttpClient simulators, CancellationToken cancellationToken)
    {
        await using var received = await simulators.GetStreamAsync("/_sim/received", cancellationToken);
        return await JsonSerializer.DeserializeAsync<SimulatorRecord>(received, JsonSerializerOptions.Web, cancellationToken)
            ?? throw new InvalidOperationException("The simulators returned no record.");
    }
}
