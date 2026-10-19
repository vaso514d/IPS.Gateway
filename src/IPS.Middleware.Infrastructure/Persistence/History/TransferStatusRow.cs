using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Infrastructure.Persistence.History;

// One append-only row of a transfer's status history: a status change (Step null) or a processing step under the
// current status, as in the original gateway's PaymentTransactionStatuses.
internal sealed class TransferStatusRow
{
    internal const int MessageTypeLength = 35;
    internal const int ReasonCodeLength = 35;
    internal const int DescriptionLength = 2000;

    public long Id { get; set; }
    public Guid PaymentTransactionId { get; set; }
    public TransactionDirection Direction { get; set; }
    public string MessageType { get; set; } = string.Empty;
    public TransactionStatus Status { get; set; }
    public StatusSource Source { get; set; }
    public ProcessingStep? Step { get; set; }
    public string? ReasonCode { get; set; }
    public int? IpsInternalCode { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset AtUtc { get; set; }
}
