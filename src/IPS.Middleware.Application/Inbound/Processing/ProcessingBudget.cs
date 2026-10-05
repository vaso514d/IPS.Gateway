namespace IPS.Middleware.Application.Inbound.Processing;

// Remote-call budgets measured back from the frozen payment deadline; a submission keeps the inline status budget in hand.
public readonly record struct ProcessingBudget(DateTimeOffset DeadlineUtc, IncomingProcessingOptions Options)
{
    public TimeSpan Submission(DateTimeOffset now) => Positive(Remaining(now) - Options.StatusBudget);

    public TimeSpan Status(DateTimeOffset now)
    {
        var remaining = Remaining(now);
        return Positive(remaining < Options.StatusBudget ? remaining : Options.StatusBudget);
    }

    public bool WithinReplyWindow(DateTimeOffset now) => now < DeadlineUtc - Options.ReplyReserve;

    private TimeSpan Remaining(DateTimeOffset now) => DeadlineUtc - now - Options.ReplyReserve;

    private static TimeSpan Positive(TimeSpan value) => value > TimeSpan.Zero ? value : TimeSpan.Zero;
}
