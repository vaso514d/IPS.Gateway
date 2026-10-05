namespace IPS.Middleware.Application.Payments.Pacs008;

public sealed class Pacs008Options
{
    public Pacs008Options(TimeSpan? submissionWindow = null, TimeSpan? ownership = null, TimeSpan? preparationRetryDelay = null, TimeSpan? persistenceBudget = null)
    {
        SubmissionWindow = Positive(submissionWindow ?? TimeSpan.FromSeconds(20), nameof(submissionWindow));
        Ownership = Positive(ownership ?? TimeSpan.FromSeconds(45), nameof(ownership));
        PersistenceBudget = Positive(persistenceBudget ?? TimeSpan.FromSeconds(2), nameof(persistenceBudget));
        if (PersistenceBudget >= Ownership) throw new ArgumentException("Persistence budget must be below ownership.");
        PreparationRetryDelay = Positive(preparationRetryDelay ?? TimeSpan.FromSeconds(1), nameof(preparationRetryDelay));
    }

    /// <summary>Time after the client's acceptance time during which an initial submission may start.</summary>
    public TimeSpan SubmissionWindow { get; }
    public TimeSpan Ownership { get; }
    public TimeSpan PreparationRetryDelay { get; }
    public TimeSpan PersistenceBudget { get; }

    private static TimeSpan Positive(TimeSpan value, string name) =>
        value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(name);
}
