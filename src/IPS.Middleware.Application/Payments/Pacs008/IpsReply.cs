using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Payments.Pacs008;

public enum IpsReplyStatus
{
    Accepted,
    Rejected,
    Unresolved
}

// The business meaning of a stored IPS response. Unresolved replies require investigation.
public sealed record IpsReply(IpsReplyStatus Status, PaymentDetails Details);

// The identifiers and original message definition a reply must reference to belong to the submitted payment.
public sealed record IpsReplyCorrelation(
    string MessageId,
    string TransactionId,
    string EndToEndId,
    string MessageDefinition = PaymentMessageTypes.Pacs008Definition);
