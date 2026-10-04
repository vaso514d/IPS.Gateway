using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Application.Payments.Pacs008;

public enum IpsReplyStatus { Accepted, Rejected, Unresolved }

/// <summary>The business meaning of a stored IPS response. Unresolved replies require investigation.</summary>
public sealed record IpsReply(IpsReplyStatus Status, PaymentDetails Details);

/// <summary>The identifiers a reply must reference to belong to the submitted payment.</summary>
public sealed record IpsReplyCorrelation(string MessageId, string TransactionId, string EndToEndId);
