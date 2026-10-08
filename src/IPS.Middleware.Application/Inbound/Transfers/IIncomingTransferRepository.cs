using IPS.Middleware.Domain.Inbound;

namespace IPS.Middleware.Application.Inbound.Transfers;

public sealed record IncomingTransferClaim(Guid TransferId, Guid Token, DateTimeOffset ExpiresAtUtc);

// A tracked transfer with its frozen content and the end of its recovery window.
public sealed record IncomingTransferSnapshot(IncomingTransfer Transfer, IIncomingTransferContent Content, DateTimeOffset DeadlineUtc);

public interface IIncomingTransferRepository
{
    // Exact identity lookup: normalized participant BIC, kind and ordinal key. The transfer is tracked in this scope.
    Task<IncomingTransferSnapshot?> FindAsync(string participantBic, string kind, string key, CancellationToken cancellationToken);

    Task<IncomingTransferSnapshot?> ReadAsync(Guid transferId, CancellationToken cancellationToken);

    void Add(IncomingTransfer transfer, IIncomingTransferContent content, DateTimeOffset deadlineUtc);

    Task<IReadOnlyList<Guid>> FindDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken);

    // Null when the transfer is not due or another owner holds it.
    Task<IncomingTransferClaim?> StageClaimAsync(Guid transferId, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken);

    // Drops ownership and sets the next due time (null: no more work); false when the claim is no longer live.
    Task<bool> StageReleaseAsync(IncomingTransferClaim claim, DateTimeOffset now, DateTimeOffset? nextActionAtUtc, CancellationToken cancellationToken);
}
