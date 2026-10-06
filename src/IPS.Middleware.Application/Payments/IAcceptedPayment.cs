namespace IPS.Middleware.Application.Payments;

// What every outgoing message type snapshots at intake and later resumes from. Implementations expose these members
// explicitly, so they never appear in the stored JSON snapshot.
public interface IAcceptedPayment
{
    string EndToEndId { get; }

    // A first submission may not start after this time; null means no pre-send deadline.
    DateTimeOffset? SubmissionDeadlineUtc { get; }

    // Identifiers the caller supplied; null when intake generates them.
    ProtocolIds? SuppliedIds { get; }
}

public sealed record ProtocolIds(string MessageId, string TransactionId);
