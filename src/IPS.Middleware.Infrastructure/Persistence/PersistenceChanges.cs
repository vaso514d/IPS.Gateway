using IPS.Middleware.Infrastructure.Persistence.Outgoing;

namespace IPS.Middleware.Infrastructure.Persistence;

internal sealed class PersistenceChanges
{
    internal List<OutgoingStatusDeliveryRow> PendingOutgoingStatuses { get; } = [];
    internal StagedChanges<OutgoingStatusDeliveryRow> StatusChanges { get; } = new();
    internal StagedChanges<InvestigationRow> InvestigationChanges { get; } = new();
    internal StagedChanges<OutgoingMessageRow> MessageChanges { get; } = new();
    internal HashSet<Guid> AuthorizedReplies { get; } = [];
    internal HashSet<Guid> AuthorizedIncomingCalls { get; } = [];
    internal HashSet<Guid> AuthorizedIncomingProcessing { get; } = [];
    internal HashSet<Guid> AuthorizedInboundWork { get; } = [];
    internal HashSet<Guid> AuthorizedIncomingPaymentWork { get; } = [];
    internal SavePhase Phase { get; set; }
    internal bool Failed { get; set; }
    internal HashSet<Guid> AuthorizedOwnership { get; } = [];
    internal Dictionary<Guid, string> AuthorizedArtifacts { get; } = [];

    internal void Complete()
    {
        InvestigationChanges.Clear();
        PendingOutgoingStatuses.Clear();
        StatusChanges.Clear();
        MessageChanges.Clear();
        AuthorizedReplies.Clear();
        AuthorizedIncomingCalls.Clear();
        AuthorizedIncomingProcessing.Clear();
        AuthorizedInboundWork.Clear();
        AuthorizedIncomingPaymentWork.Clear();
        AuthorizedOwnership.Clear();
        AuthorizedArtifacts.Clear();
    }

}

internal enum SavePhase
{
    Idle, Entities, Evidence
}
