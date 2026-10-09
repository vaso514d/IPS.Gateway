namespace IPS.Middleware.Performance;

// One answer of /health/ready; StatusCode is null when the instance did not answer.
internal sealed record ReadinessSample(DateTimeOffset AtUtc, int Instance, int? StatusCode, string Answer)
{
    public bool Healthy => StatusCode == 200 && Answer == "Healthy";
}

// Asks every instance for its readiness every few seconds for the whole run, so "Healthy throughout" is evidence.
internal static class Readiness
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    internal static async Task<IReadOnlyList<ReadinessSample>> SampleAsync(IReadOnlyList<HttpClient> probes, CancellationToken stop)
    {
        var samples = new List<ReadinessSample>();
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                samples.AddRange(await Task.WhenAll(probes.Select((probe, index) => ProbeAsync(probe, index + 1, stop))));
            }
            while (await timer.WaitForNextTickAsync(stop));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The run is over; a round cut short by the stop is not a sample.
        }

        return samples;
    }

    private static async Task<ReadinessSample> ProbeAsync(HttpClient probe, int instance, CancellationToken stop)
    {
        var atUtc = DateTimeOffset.UtcNow;
        try
        {
            using var response = await probe.GetAsync("/health/ready", stop);
            return new(atUtc, instance, (int)response.StatusCode, await response.Content.ReadAsStringAsync(stop));
        }
        catch (Exception error) when ((error is HttpRequestException or TaskCanceledException) && !stop.IsCancellationRequested)
        {
            return new(atUtc, instance, null, error.Message);
        }
    }
}
