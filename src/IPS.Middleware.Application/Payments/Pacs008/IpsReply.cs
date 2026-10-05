using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Payments.Pacs008;

public enum IpsReplyStatus
{
    Accepted,
    Rejected,
    Unresolved
}

/// <summary>The business meaning of a stored IPS response. Unresolved replies require investigation.</summary>
public sealed class IpsReply
{
    public IpsReply(IpsReplyStatus status, PaymentDetails details)
    {
        Status = status;
        Details = details;
    }

    public IpsReplyStatus Status { get; init; }
    public PaymentDetails Details { get; init; }
}

/// <summary>The identifiers a reply must reference to belong to the submitted payment.</summary>
public sealed class IpsReplyCorrelation
{
    [System.Text.Json.Serialization.JsonConstructor]
    public IpsReplyCorrelation(string messageId, string transactionId, string endToEndId)
    {
        MessageId = messageId;
        TransactionId = transactionId;
        EndToEndId = endToEndId;
    }

    public string MessageId { get; init; }
    public string TransactionId { get; init; }
    public string EndToEndId { get; init; }

    public IpsReplyCorrelation(IpsReplyCorrelation original)
    {
        MessageId = original.MessageId;
        TransactionId = original.TransactionId;
        EndToEndId = original.EndToEndId;
    }
}
