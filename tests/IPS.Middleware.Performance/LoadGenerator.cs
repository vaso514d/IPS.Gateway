using System.Diagnostics;
using System.Net.Http.Json;
using IPS.MiidleWear.Contracts.Pacs008;

namespace IPS.Middleware.Performance;

internal enum Phase
{
    WarmUp,
    Measured
}

// A stretch of the schedule at one rate.
internal sealed record LoadPhase(Phase Phase, int Rate, TimeSpan Duration)
{
    internal int Count => (int)(Duration.TotalSeconds * Rate);
}

// One payment the generator sent and what the API answered: the status code and body, or, when no response arrived, the failure
// and the time until it (Elapsed).
internal sealed record SentPayment(
    string Reference,
    string EndToEndId,
    Phase Phase,
    int Instance,
    TimeSpan Lag,
    DateTimeOffset SentAtUtc,
    TimeSpan Elapsed,
    int? StatusCode,
    string? Body,
    string? Failure);

// The open-model load: each request starts at its planned time whatever the latency of the ones before, so a slow service cannot
// quietly lower the load. Requests go round-robin over the instances, and every request records its lag behind the plan.
internal static class LoadGenerator
{
    internal static async Task<IReadOnlyList<SentPayment>> RunAsync(
        IReadOnlyList<HttpClient> instances,
        IReadOnlyList<LoadPhase> phases,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        var requests = new List<Task<SentPayment>>();
        var started = Stopwatch.GetTimestamp();
        var phaseStart = TimeSpan.Zero;
        foreach (var phase in phases)
        {
            log.WriteLine($"{DateTimeOffset.UtcNow:HH:mm:ss} {phase.Phase}: {phase.Count} payments at {phase.Rate} per second.");
            for (var index = 0; index < phase.Count; index++)
            {
                var planned = phaseStart + TimeSpan.FromSeconds((double)index / phase.Rate);
                await WaitUntilAsync(started, planned, cancellationToken);
                var instance = requests.Count % instances.Count;
                requests.Add(SendAsync(instances[instance], instance + 1, phase.Phase, started, planned, cancellationToken));
            }

            phaseStart += phase.Duration;
        }

        return await Task.WhenAll(requests);
    }

    // Task.Delay wakes on the system timer (about 15 ms on Windows), so a request can start up to one tick late, or a fraction of a
    // millisecond early (a negative lag) when the timer and the stopwatch disagree; its lag shows either.
    private static async Task WaitUntilAsync(long started, TimeSpan planned, CancellationToken cancellationToken)
    {
        var remaining = planned - Stopwatch.GetElapsedTime(started);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, cancellationToken);
        }
    }

    // Called on the schedule and runs synchronously up to the network, so the start it records is when the request left.
    private static async Task<SentPayment> SendAsync(
        HttpClient instance,
        int number,
        Phase phase,
        long started,
        TimeSpan planned,
        CancellationToken cancellationToken)
    {
        var reference = "perf-" + Guid.NewGuid().ToString("N")[..12];
        var endToEndId = "E2E-" + reference;
        var request = Pacs008(reference, endToEndId);
        var lag = Stopwatch.GetElapsedTime(started) - planned;
        var sentAtUtc = DateTimeOffset.UtcNow;
        var sending = Stopwatch.GetTimestamp();
        try
        {
            using var response = await instance.PostAsJsonAsync(Pacs008RestApiRoutes.Send, request, cancellationToken);
            var elapsed = Stopwatch.GetElapsedTime(sending);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new(reference, endToEndId, phase, number, lag, sentAtUtc, elapsed, (int)response.StatusCode, body, null);
        }
        catch (Exception error) when ((error is HttpRequestException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            return new(reference, endToEndId, phase, number, lag, sentAtUtc, Stopwatch.GetElapsedTime(sending), null, null, error.Message);
        }
    }

    // A valid outgoing pacs.008; the unique client reference also makes its end-to-end id unique.
    private static Pacs008InstantPaymentRequestDto Pacs008(string reference, string endToEndId)
    {
        var now = DateTimeOffset.UtcNow;
        return new()
        {
            ClientReference = reference,
            InstructionId = "BANK-" + reference,
            EndToEndId = endToEndId,
            CreationDateTime = now.AddMilliseconds(-500),
            AcceptanceDateTime = now,
            Amount = 12.34m,
            Currency = "GEL",
            InstructionPriority = "HIGH",
            Debtor = new() { Type = 1, Name = "Debtor", Account = "GE95TB0000000123456789" },
            Creditor = new() { Type = 1, Name = "Creditor", ParticipantBic = "TBCBGE22", Account = "GE29NB0000000101904917" }
        };
    }
}
