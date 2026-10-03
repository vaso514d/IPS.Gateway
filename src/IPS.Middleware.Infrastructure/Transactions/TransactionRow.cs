using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Infrastructure.Transactions;

internal sealed class TransactionRow
{
    public Guid Id { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public TransactionDirection Direction { get; set; }
    public string ClientReference { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string RequestJson { get; set; } = string.Empty;
    public TransactionStatus CurrentStatus { get; set; }
    public DateTimeOffset CurrentStatusAtUtc { get; set; }
    public int CurrentSequence { get; set; }
    public int LastSequence { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<TransactionHistoryRow> History { get; set; } = [];

    public void SetCurrent(PaymentTransaction transaction)
    {
        CurrentStatus = transaction.Current.Status;
        CurrentStatusAtUtc = transaction.Current.AtUtc;
        CurrentSequence = transaction.Current.Sequence;
        LastSequence = transaction.History.Count;
    }

    public PaymentTransaction ToDomain()
    {
        var transaction = new PaymentTransaction(Id, MessageType, Direction, CreatedAtUtc, ClientReference);
        foreach (var row in History.OrderBy(entry => entry.Sequence))
        {
            var entry = row.Sequence == 1
                ? transaction.Current
                : row.Step is { } step
                    ? transaction.RecordStep(step, row.AtUtc)
                    : transaction.ChangeStatus(row.Status, row.Source, row.AtUtc,
                        row.ReasonCode, row.IpsInternalCode, row.Description);

            if (!row.Matches(entry))
            {
                throw new InvalidOperationException($"Transaction {Id} has inconsistent history at sequence {row.Sequence}.");
            }
        }

        if (History.Count == 0 || History.Count != LastSequence || transaction.History.Count != History.Count ||
            transaction.Current.Sequence != CurrentSequence || transaction.Current.Status != CurrentStatus ||
            transaction.Current.AtUtc != CurrentStatusAtUtc)
        {
            throw new InvalidOperationException($"Transaction {Id} has inconsistent current status and history.");
        }

        return transaction;
    }
}

internal sealed class TransactionHistoryRow
{
    public Guid TransactionId { get; set; }
    public int Sequence { get; set; }
    public TransactionStatus Status { get; set; }
    public StatusSource Source { get; set; }
    public DateTimeOffset AtUtc { get; set; }
    public ProcessingStep? Step { get; set; }
    public string? ReasonCode { get; set; }
    public int? IpsInternalCode { get; set; }
    public string? Description { get; set; }

    public static TransactionHistoryRow From(Guid transactionId, TransactionHistoryEntry entry) => new()
    {
        TransactionId = transactionId,
        Sequence = entry.Sequence,
        Status = entry.Status,
        Source = entry.Source,
        AtUtc = entry.AtUtc,
        Step = entry.Step,
        ReasonCode = entry.ReasonCode,
        IpsInternalCode = entry.IpsInternalCode,
        Description = entry.Description
    };

    public bool Matches(TransactionHistoryEntry entry) =>
        Sequence == entry.Sequence && Status == entry.Status && Source == entry.Source &&
        AtUtc == entry.AtUtc && Step == entry.Step && ReasonCode == entry.ReasonCode &&
        IpsInternalCode == entry.IpsInternalCode && Description == entry.Description;
}
