using System.Collections.ObjectModel;

namespace IPS.Middleware.Domain.Transactions;

public sealed class PaymentTransaction
{
    private readonly List<TransactionHistoryEntry> _history = [];
    private readonly ReadOnlyCollection<TransactionHistoryEntry> _historyView;

    public PaymentTransaction(
        Guid id,
        string messageType,
        TransactionDirection direction,
        DateTimeOffset receivedAt,
        string? clientReference = null,
        string? coreReference = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Transaction identity is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(messageType))
        {
            throw new ArgumentException("Message type is required.", nameof(messageType));
        }

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        Id = id;
        MessageType = messageType.Trim();
        Direction = direction;
        ClientReference = NormalizeReference(clientReference);
        CoreReference = NormalizeReference(coreReference);
        CreatedAtUtc = receivedAt.ToUniversalTime();
        _historyView = _history.AsReadOnly();
        Current = Append(TransactionStatus.Received, StatusSource.Gateway, receivedAt);
    }

    public Guid Id { get; }
    public string MessageType { get; }
    public TransactionDirection Direction { get; }
    public string? ClientReference { get; }
    public string? CoreReference { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public TransactionHistoryEntry Current { get; private set; }
    public IReadOnlyList<TransactionHistoryEntry> History => _historyView;

    public bool IsFinal => Current.Status is TransactionStatus.Accepted
        or TransactionStatus.Rejected
        or TransactionStatus.NotSent
        or TransactionStatus.ManuallyResolved;

    public TransactionHistoryEntry ChangeStatus(
        TransactionStatus status,
        StatusSource source,
        DateTimeOffset at,
        string? reasonCode = null,
        int? ipsInternalCode = null,
        string? description = null)
    {
        // Eligibility depends on the workflow; IsFinal alone does not prohibit later observations.
        Current = Append(status, source, at, reasonCode: reasonCode,
            ipsInternalCode: ipsInternalCode, description: description);
        return Current;
    }

    public TransactionHistoryEntry RecordStep(ProcessingStep step, DateTimeOffset at) =>
        Append(Current.Status, StatusSource.Gateway, at, step);

    private TransactionHistoryEntry Append(
        TransactionStatus status,
        StatusSource source,
        DateTimeOffset at,
        ProcessingStep? step = null,
        string? reasonCode = null,
        int? ipsInternalCode = null,
        string? description = null)
    {
        var entry = new TransactionHistoryEntry(_history.Count + 1, status, source, at,
            step, reasonCode, ipsInternalCode, description);
        _history.Add(entry);
        return entry;
    }

    private static string? NormalizeReference(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
