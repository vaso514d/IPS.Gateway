namespace IPS.Middleware.Application.Inbound.Processing;
/// <summary>Remote-call budgets measured back from the frozen payment deadline; a submission keeps the inline status budget in hand.</summary>
public sealed class ProcessingBudget
{
    public ProcessingBudget(DateTimeOffset deadlineUtc, IncomingProcessingOptions options)
    {
        DeadlineUtc = deadlineUtc;
        Options = options;
    }

    public DateTimeOffset DeadlineUtc { get; init; }
    public IncomingProcessingOptions Options { get; init; }

    public TimeSpan Submission(DateTimeOffset now) => Positive(Remaining(now) - Options.StatusBudget);
    public TimeSpan Status(DateTimeOffset now) => Positive(TimeSpan.FromTicks(Math.Min(Remaining(now).Ticks, Options.StatusBudget.Ticks)));
    public bool WithinReplyWindow(DateTimeOffset now) => now < DeadlineUtc - Options.ReplyReserve;
    private TimeSpan Remaining(DateTimeOffset now) => DeadlineUtc - now - Options.ReplyReserve;
    private static TimeSpan Positive(TimeSpan value) => value > TimeSpan.Zero ? value : TimeSpan.Zero;
}
