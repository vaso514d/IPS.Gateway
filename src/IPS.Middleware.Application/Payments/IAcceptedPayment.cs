using IPS.Middleware.Application.Payments.Pacs008;

namespace IPS.Middleware.Application.Payments;

// What every outgoing message type snapshots at intake and later resumes from. Implementations expose these members
// explicitly, so they never appear in the stored JSON snapshot.
public interface IAcceptedPayment
{
    // The end-to-end id shown in the payment's status; for a return, the returned payment's.
    string EndToEndId { get; }

    // A first submission may not start after this time; null means no pre-send deadline.
    DateTimeOffset? SubmissionDeadlineUtc { get; }

    // Identifiers the caller supplied; null when intake generates them.
    ProtocolIds? SuppliedIds { get; }

    // What the pacs.002 about this payment must reference to belong to it, given its stored protocol ids.
    IpsReplyCorrelation ReplyCorrelation(string messageId, string transactionId);
}

public sealed record ProtocolIds(string MessageId, string TransactionId);
