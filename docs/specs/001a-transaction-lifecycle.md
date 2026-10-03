# Capability 1a: transaction state and history

Status: Ready for owner review; independent Standards and Spec reviews have no remaining findings. Owner merge approval pending.

This is the first review within [capability 1](../migration-ledger.md). It establishes the domain model before capability 1b introduces atomic intake, SQL persistence, concurrency, and restart recovery. It adds no HTTP endpoint, background worker, storage adapter, or contract mapping.

## Source evidence

All paths refer to source commit `d498de6c4638aa71cdb20189d13642b41abab5f1` in [the original repository](https://github.com/vaso514d/IPS.MiidleWear/tree/d498de6c4638aa71cdb20189d13642b41abab5f1).

- `IPS.MiidleWear.Domain/Entities/Transactions/PaymentTransaction.cs`: construction, ChangeStatus, RecordStep, IsFinal, and normalization.
- `IPS.MiidleWear.Domain/Entities/Transactions/PaymentTransactionStatusRecord.cs`: status/step history, UTC timestamps, reason casing, and bounded descriptions.
- `IPS.MiidleWear.Domain/Entities/Transactions/PaymentTransactionEnums.cs`: lifecycle states, origins, and processing milestones.
- `IPS.MiidleWear.Tests/Domain/PaymentTransactionTests.cs`: Received -> XML generation -> Sending -> Rejected, with append-only history and normalized rejection reason. Delivery retries and Annex 2 error actions belong to later capabilities.
- `IPS.MiidleWear.Gateway/Services/IncomingStatusReportApplier.cs` and `IPS.MiidleWear.Tests/Gateway/IncomingStatusReportApplierTests.cs`: the incoming status-report workflow explicitly ignores a conflicting report after a final outcome.
- `IPS.MiidleWear.Application/Transactions/TransactionStatusMapping.cs`: internal recovery states are exposed as Processing; no mapping is moved into Domain.
- `IPS.MiidleWear.Persistence/Configurations/PaymentTransactionStatusRecordConfiguration.cs`: the old history uses database identity to distinguish observations with equal timestamps.

## Behavior

1. Creation records Received from Gateway at the supplied time. The caller supplies the transaction identity and observation times; the model reads no clock or ambient activity.
2. Message type and optional client/core references are trimmed; blank references become null. Unknown nonblank message names remain allowed, as in the source. Message-specific validation comes with its capability.
3. Each status change appends an immutable history entry and becomes the current status observation. Each processing step appends the current state from Gateway without replacing the current observation or copying its reason fields.
4. Observation times are stored in UTC. Entry sequence records append order even when timestamps are equal or clocks move backward. Prior entries cannot be edited or removed through the model's public interface.
5. Reasons are trimmed and uppercased. Blank reasons/descriptions become null. Descriptions are trimmed and bounded to 2,000 characters, preserving the old observable text behavior. Normalization happens once when creating an entry, so current and historical status details agree.
6. A new status clears earlier reason/code/description values when replacements are absent. Repeated statuses append separate observations; the model does not deduplicate them.
7. Accepted, Rejected, NotSent, and ManuallyResolved are final. Received, Sending, Uncertain, Investigating, Resending, ManualReview, and CoreUnknown are not final.
8. The model permits all defined status changes, including changes from a final state. Workflow-specific eligibility remains Application's responsibility. Empty identities and undefined direction/status/source/step enum values are programming errors and fail before changing history.

## Architecture and scope

Domain owns the transaction, immutable observations, and lifecycle vocabulary. Its history is a read-only live view. The current status references the actual status-change entry rather than maintaining a second copy of the same data.

The model contains no EF constructors, row versions, navigation properties, database history identities, document links, XML, HTTP, trace capture, scheduler state, retry counters, or callback-delivery policy. Those concerns arrive in their owning layer when implemented. Wire identifiers, amounts, and message-specific facts will be added with intake/payment capabilities when needed.

The unit test project gains an explicit Domain reference because transitive project references are disabled. No production dependency or package is added. Contracts remain unchanged.

## Gaps and decisions

- The source entity permits any status change, while particular workflows reject conflicting final results. There is no universal transition graph established by these sources. This PR preserves the entity behavior rather than inventing a graph; later workflow specifications must characterize their own eligibility rules.
- The old entity exposes its mutable List as IReadOnlyCollection, allowing a caller to cast and mutate it. The replacement protects the collection and makes entries immutable. This is an internal encapsulation improvement, not an approved payment-behavior change.
- The old entity uses the same model as an EF entity and couples retry/delivery state to it. Persistence records and application processing decisions will be designed separately in 1b and later capabilities.
- Append order is explicit in memory. SQL history ordering and concurrency are unimplemented and unverified here; 1b must test them against SQL Server. No durability or multi-instance guarantee is claimed by this PR.

Approved external behavior differences: none.

## Acceptance scenarios

- Normalize an outgoing transaction and create its first UTC status observation; create an incoming transaction without optional references.
- Record the original rejection scenario and retain all status/step observations in sequence.
- Preserve current outcome details while recording a processing step; clear them on a new status without explanation.
- Preserve prior immutable observations and prevent removal through a cast of the history collection.
- Preserve repeated and equal-time observations, including a later append with an earlier clock time.
- Classify every lifecycle state; retain the source's permissive final-state replacement behavior.
- Normalize blank/lowercase reasons and blank/overlong descriptions consistently.
- Reject invalid internal arguments without partially mutating an existing transaction.
- Keep architecture, Contracts compatibility, and host tests passing.
