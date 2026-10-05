namespace IPS.Middleware.Application.Inbound.Processing;

public sealed class IncomingProcessingOptions
{
    public IncomingProcessingOptions(
        TimeSpan? paymentWindow = null,
        TimeSpan? statusBudget = null,
        TimeSpan? replyReserve = null,
        TimeSpan? ownership = null,
        TimeSpan? persistenceBudget = null,
        TimeSpan? followUpDelay = null)
    {
        PaymentWindow = paymentWindow ?? TimeSpan.FromSeconds(20);
        StatusBudget = statusBudget ?? TimeSpan.FromSeconds(3);
        ReplyReserve = replyReserve ?? TimeSpan.FromSeconds(2);
        Ownership = ownership ?? TimeSpan.FromSeconds(45);
        PersistenceBudget = persistenceBudget ?? TimeSpan.FromSeconds(2);
        FollowUpDelay = followUpDelay ?? TimeSpan.FromSeconds(10);
        TimeSpan[] budgets = [PaymentWindow, StatusBudget, ReplyReserve, PersistenceBudget, FollowUpDelay];
        var reservesReplyTime = StatusBudget + ReplyReserve < PaymentWindow;
        var fitsOwnership = Ownership > PaymentWindow + PersistenceBudget;
        if (budgets.Any(budget => budget <= TimeSpan.Zero) || !reservesReplyTime || !fitsOwnership)
        {
            throw new ArgumentException("Positive budgets must reserve reply time and fit within ownership.");
        }
    }

    public TimeSpan PaymentWindow { get; }
    public TimeSpan StatusBudget { get; }
    public TimeSpan ReplyReserve { get; }
    public TimeSpan Ownership { get; }
    public TimeSpan PersistenceBudget { get; }
    public TimeSpan FollowUpDelay { get; }
}
