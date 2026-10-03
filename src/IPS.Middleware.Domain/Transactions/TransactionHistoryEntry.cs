namespace IPS.Middleware.Domain.Transactions;

public sealed class TransactionHistoryEntry
{
    internal TransactionHistoryEntry(
        int sequence,
        TransactionStatus status,
        StatusSource source,
        DateTimeOffset at,
        ProcessingStep? step = null,
        string? reasonCode = null,
        int? ipsInternalCode = null,
        string? description = null)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        if (step is { } milestone && !Enum.IsDefined(milestone))
        {
            throw new ArgumentOutOfRangeException(nameof(step));
        }

        Sequence = sequence;
        Status = status;
        Source = source;
        AtUtc = at.ToUniversalTime();
        Step = step;
        ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? null : reasonCode.Trim().ToUpperInvariant();
        IpsInternalCode = ipsInternalCode;
        var text = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Description = text is { Length: > 2000 } ? text[..2000] : text;
    }

    public int Sequence { get; }
    public TransactionStatus Status { get; }
    public StatusSource Source { get; }
    public DateTimeOffset AtUtc { get; }
    public ProcessingStep? Step { get; }
    public string? ReasonCode { get; }
    public int? IpsInternalCode { get; }
    public string? Description { get; }
}
