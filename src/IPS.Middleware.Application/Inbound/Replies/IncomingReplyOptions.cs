namespace IPS.Middleware.Application.Inbound.Replies;

public sealed class IncomingReplyOptions
{
    public IncomingReplyOptions(
        int maxAttempts = 2,
        TimeSpan? retryDelay = null,
        TimeSpan? callTimeout = null,
        TimeSpan? persistenceBudget = null,
        TimeSpan? ownership = null,
        TimeSpan? preparationRetryDelay = null)
    {
        MaxAttempts = maxAttempts;
        RetryDelay = retryDelay ?? TimeSpan.FromMilliseconds(200);
        CallTimeout = callTimeout ?? TimeSpan.FromSeconds(20);
        PersistenceBudget = persistenceBudget ?? TimeSpan.FromSeconds(2);
        Ownership = ownership ?? TimeSpan.FromSeconds(45);
        PreparationRetryDelay = preparationRetryDelay ?? TimeSpan.FromSeconds(5);
        TimeSpan[] budgets = [RetryDelay, CallTimeout, PersistenceBudget, PreparationRetryDelay];
        var fitsOwnership = Ownership > CallTimeout + PersistenceBudget;
        if (MaxAttempts <= 0 || budgets.Any(budget => budget <= TimeSpan.Zero) || !fitsOwnership)
        {
            throw new ArgumentException("Positive reply attempts and budgets must fit within ownership.");
        }
    }

    public int MaxAttempts { get; }
    public TimeSpan RetryDelay { get; }
    public TimeSpan CallTimeout { get; }
    public TimeSpan PersistenceBudget { get; }
    public TimeSpan Ownership { get; }
    public TimeSpan PreparationRetryDelay { get; }
}
