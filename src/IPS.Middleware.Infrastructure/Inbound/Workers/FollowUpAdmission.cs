namespace IPS.Middleware.Infrastructure.Inbound.Workers;

internal enum FollowUpKind
{
    Reconciliation,
    Transfer
}

internal sealed record FollowUpWork(Guid Id, FollowUpKind Kind);

// Chooses which due CBS follow-up work takes the free slots. Each slot goes to the next due item of the kind not admitted
// last, so a backlog of one kind cannot take every free slot sweep after sweep; a kind with nothing due leaves its turn.
internal sealed class FollowUpAdmission
{
    private FollowUpKind _next = FollowUpKind.Reconciliation;

    // Work that is already running is not due again and uses no slot.
    internal IReadOnlyList<FollowUpWork> Select(IReadOnlyList<Guid> payments, IReadOnlyList<Guid> transfers, int free, Func<Guid, bool> running)
    {
        var due = new Dictionary<FollowUpKind, Queue<Guid>>
        {
            [FollowUpKind.Reconciliation] = new(payments.Where(id => !running(id))),
            [FollowUpKind.Transfer] = new(transfers.Where(id => !running(id)))
        };
        var selected = new List<FollowUpWork>();
        while (selected.Count < free && due.Values.Any(queue => queue.Count > 0))
        {
            var kind = due[_next].Count > 0 ? _next : Other(_next);
            selected.Add(new(due[kind].Dequeue(), kind));
            _next = Other(kind);
        }

        return selected;
    }

    private static FollowUpKind Other(FollowUpKind kind) =>
        kind == FollowUpKind.Transfer ? FollowUpKind.Reconciliation : FollowUpKind.Transfer;
}
