using IPS.Middleware.Application.Transactions;
using IPS.Middleware.Domain.Transactions;

namespace IPS.Middleware.Infrastructure.Persistence.Outgoing;

internal sealed class OutgoingPaymentMetadata
{
    public Guid Id { get; set; }
    public OutgoingPayment Payment { get; set; } = null!;
    public string RequestJson { get; set; } = null!;
    public string? AcceptedJson { get; set; }
    public TransactionDirection Direction { get; set; }
    public string? MessageId { get; set; }
    public string? ProtocolTransactionId { get; set; }
    public string? UnsignedXml { get; set; }
    public Guid? ClaimToken { get; set; }
    public DateTimeOffset? ClaimExpiresAtUtc { get; set; }
    public DateTimeOffset? NextActionAtUtc { get; set; }

    // Read-only copies of columns of the same row for discovery, which reads them untracked: the payment writes its status and
    // SQL computes the dispatch priority (0 for pacs.008, else 1).
    public TransactionStatus CurrentStatus { get; private set; }
    public DateTimeOffset CurrentStatusAtUtc { get; private set; }
    public int DispatchPriority { get; private set; }
    public byte[] RowVersion { get; set; } = [];

    public bool IsClaimLive(DateTimeOffset now) => ClaimToken is not null && ClaimExpiresAtUtc > now;

    public bool HasLiveClaim(TransactionClaim claim, DateTimeOffset now) =>
        claim.Token != Guid.Empty && claim.TransactionId == Id && ClaimToken == claim.Token && IsClaimLive(now);
}
