namespace IPS.Middleware.Performance;

// Exact order statistics of raw samples, in milliseconds and unrounded, so the verdict compares what was observed. A percentile
// is the nearest-rank sample: the smallest sample with at least that share of all samples at or below it.
internal sealed record Distribution(int Samples, double Min, double Mean, double P50, double P95, double P99, double Max)
{
    internal static Distribution Of(IEnumerable<TimeSpan> samples)
    {
        var sorted = samples
            .Select(sample => sample.TotalMilliseconds)
            .Order()
            .ToArray();
        if (sorted.Length == 0)
        {
            return new(0, 0, 0, 0, 0, 0, 0);
        }

        return new(sorted.Length, sorted[0], sorted.Average(), Rank(sorted, 50), Rank(sorted, 95), Rank(sorted, 99), sorted[^1]);
    }

    private static double Rank(double[] sorted, int percent) => sorted[(percent * sorted.Length + 99) / 100 - 1];
}

// What the run achieved in the measured window. MaxInFlight counts the generator's open requests (warm-up included); arrivals at
// full slots are requests that started while at least as many requests were open as all instances have execution slots, so
// their payments may have waited for admission (the concurrency risk of 013).
internal sealed record Throughput(
    double PlannedRate,
    double SentPerSecond,
    double CallbacksPerSecond,
    int ExecutionSlots,
    int MaxInFlight,
    int ArrivalsAtFullSlots)
{
    internal static Throughput Of(IReadOnlyList<PaymentOutcome> outcomes, SimulatorRecord received, int plannedRate, int executionSlots)
    {
        var measured = outcomes
            .Select(outcome => outcome.Payment)
            .Where(payment => payment.Phase == Phase.Measured)
            .ToArray();
        if (measured.Length < 2)
        {
            return new(plannedRate, 0, 0, executionSlots, 0, 0);
        }

        var first = measured.Min(payment => payment.SentAtUtc);
        var last = measured.Max(payment => payment.SentAtUtc);
        var window = (last - first).TotalSeconds;
        var callbacks = received.Callbacks.Count(callback => callback.ReceivedAtUtc >= first && callback.ReceivedAtUtc <= last);
        var concurrency = OpenAtEachStart(outcomes.Select(outcome => outcome.Payment).ToArray());
        return new(
            plannedRate,
            Math.Round((measured.Length - 1) / window, 2),
            Math.Round(callbacks / window, 2),
            executionSlots,
            concurrency.Max(),
            concurrency.Count(open => open > executionSlots));
    }

    // How many requests were open, the new one included, when each request started.
    private static int[] OpenAtEachStart(IReadOnlyList<SentPayment> payments)
    {
        var ends = payments
            .Select(payment => payment.SentAtUtc + payment.Elapsed)
            .Order()
            .ToArray();
        var starts = payments
            .Select(payment => payment.SentAtUtc)
            .Order()
            .ToArray();
        var open = new int[starts.Length];
        var ended = 0;
        for (var index = 0; index < starts.Length; index++)
        {
            while (ended < ends.Length && ends[ended] <= starts[index])
            {
                ended++;
            }

            open[index] = index + 1 - ended;
        }

        return open;
    }
}
