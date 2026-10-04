using IPS.Middleware.Application.Inbound.Pacs008;

namespace IPS.Middleware.Application.Inbound.Receipts;

public interface IInboundWorkRepository
{
    Task<IReadOnlyList<Guid>> FindDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken);
    Task<InboundClaim?> StageClaimAsync(Guid journalId, DateTimeOffset now, TimeSpan duration, CancellationToken cancellationToken);
    Task<bool> StageFinishAsync(InboundClaim claim, DateTimeOffset now, DateTimeOffset? nextActionAtUtc, CancellationToken cancellationToken);
    /// <summary>The receipt behind a live claim, tracked for staged changes in this scope; otherwise null.</summary>
    Task<OwnedInboundReceipt?> FindOwnedAsync(InboundClaim claim, DateTimeOffset now, CancellationToken cancellationToken);
    /// <summary>Stage a held disposition and drop ownership and scheduling; false when the claim is no longer live.</summary>
    Task<bool> StageHoldAsync(InboundClaim claim, DateTimeOffset now, string reason, CancellationToken cancellationToken);
    /// <summary>Save trusted original references once under a live claim; identical repeats do not change them.</summary>
    Task<bool> StageOriginalReferencesAsync(InboundClaim claim, IncomingPacs008Reference original, DateTimeOffset now,
        CancellationToken cancellationToken);
    /// <summary>
    /// Attach an owned receipt with saved original references to a payment, once. Repeating the same attachment changes
    /// nothing; attaching it to another payment throws. False when the claim is no longer live.
    /// </summary>
    Task<bool> StageAttachmentAsync(InboundClaim claim, Guid paymentId, DateTimeOffset now, CancellationToken cancellationToken);
}
