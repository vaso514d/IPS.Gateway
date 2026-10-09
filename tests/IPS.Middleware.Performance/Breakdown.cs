using System.Globalization;

namespace IPS.Middleware.Performance;

// One instance's share of the run, so a stall on one instance is visible. The first failure is the send time of the first
// payment answered other than 200, not sent, lost or sent twice.
internal sealed record InstanceOutcome(
    int Instance,
    int Sent,
    IReadOnlyDictionary<string, int> Responses,
    int AcceptedNotSent,
    int Lost,
    int DuplicateCallbacks,
    DateTimeOffset? FirstFailureAtUtc)
{
    internal static IReadOnlyList<InstanceOutcome> Of(IReadOnlyList<PaymentOutcome> outcomes) => outcomes
        .GroupBy(outcome => outcome.Payment.Instance)
        .OrderBy(instance => instance.Key)
        .Select(instance => new InstanceOutcome(
            instance.Key,
            instance.Count(),
            Counts.Of(instance.Select(outcome => outcome.Payment.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "no response")),
            instance.Count(outcome => outcome.NotSent),
            instance.Count(outcome => outcome.Lost),
            instance.Count(outcome => outcome.ReportedTwice),
            instance
                .Where(IsFailure)
                .Select(outcome => (DateTimeOffset?)outcome.Payment.SentAtUtc)
                .Min()))
        .ToArray();

    private static bool IsFailure(PaymentOutcome outcome) =>
        outcome.Payment.StatusCode != 200 || outcome.NotSent || outcome.Lost || outcome.SentTwice;
}

// One minute of the run by send time, warm-up included. Settlement p95 is over that minute's payments with a final callback.
internal sealed record MinuteOutcome(
    DateTimeOffset FromUtc,
    Phase Phase,
    int Sent,
    int Ok,
    int GatewayTimeouts,
    int ServerErrors,
    int OtherAnswers,
    int NotSent,
    int DuplicateCallbacks,
    double SettlementP95)
{
    internal static IReadOnlyList<MinuteOutcome> Of(IReadOnlyList<PaymentOutcome> outcomes)
    {
        if (outcomes.Count == 0)
        {
            return [];
        }

        var start = outcomes.Min(outcome => outcome.Payment.SentAtUtc);
        return outcomes
            .GroupBy(outcome => (int)(outcome.Payment.SentAtUtc - start).TotalMinutes)
            .OrderBy(minute => minute.Key)
            .Select(minute => new MinuteOutcome(
                start.AddMinutes(minute.Key),
                minute.First().Payment.Phase,
                minute.Count(),
                minute.Count(outcome => outcome.Payment.StatusCode == 200),
                minute.Count(outcome => outcome.Payment.StatusCode == 504),
                minute.Count(outcome => outcome.Payment.StatusCode is >= 500 and not 504),
                minute.Count(outcome => outcome.Payment.StatusCode is not (200 or >= 500)),
                minute.Count(outcome => outcome.NotSent),
                minute.Count(outcome => outcome.ReportedTwice),
                Distribution.Of(minute
                    .Select(outcome => outcome.SettlementLatency)
                    .OfType<TimeSpan>()).P95))
            .ToArray();
    }
}
