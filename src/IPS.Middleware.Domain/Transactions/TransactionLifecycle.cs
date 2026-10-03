namespace IPS.Middleware.Domain.Transactions;

public enum TransactionDirection
{
    Incoming = 1,
    Outgoing = 2
}

public enum TransactionStatus
{
    Received = 1,
    Sending = 2,
    Accepted = 3,
    Rejected = 4,
    NotSent = 5,
    Uncertain = 6,
    Investigating = 7,
    Resending = 8,
    ManualReview = 9,
    ManuallyResolved = 10,
    CoreUnknown = 11
}

public enum StatusSource
{
    Gateway = 1,
    Ips = 2,
    Investigation = 3,
    Core = 4,
    Operator = 5,
    Recovery = 6
}

public enum ProcessingStep
{
    Received = 1,
    Validated = 2,
    XmlGenerated = 3,
    Signed = 4,
    Sent = 5,
    IpsResponded = 6,
    Parsed = 7,
    SentToCore = 8,
    CoreResponded = 9,
    RepliedToIps = 10
}
