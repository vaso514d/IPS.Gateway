# Capability 1c: durable pending work and abandoned claims

Branch: codex/capability-rebuild. Base: cdd52f9. This is a capability-led slice; earlier branches remain unmerged. Owner approval is required before merging.

## Source evidence

Read the original repository at d498de6c4638aa71cdb20189d13642b41abab5f1:
- IPS.MiidleWear.Persistence/Transactions/PaymentTransactionStore.cs, FindIdsAsync: bounded queries prioritize pacs.008, then other types, by current status time.
- IPS.MiidleWear.API/Transactions/OutgoingTransactionDispatcher.cs, OutgoingTransactionProcessor: Received and due work is claimed through an optimistic update to Sending before remote processing.
- IPS.MiidleWear.API/Transactions/TransactionRecovery.cs, ReleaseStuckAsync and TransactionRecoveryWorker.RunOnceAsync: Sending/Investigating/Resending left in flight becomes Uncertain and due; release itself sends nothing.
- IPS.MiidleWear.Tests/Api/TransactionRecoveryTests.cs: work below the configured age remains in flight; work beyond it becomes Uncertain.
- Source changes 0202969 and 5e1e748 establish durable dispatch and pacs.008 query priority.

Source gap: status-age recovery has no ownership token, and several remote completion writes are unconditional. A late worker can write after recovery. The internal redesign adds ownership fencing; external routes, JSON, callback meanings, and protocol behavior remain unchanged. Eligibility at the exact expiry boundary is explicit: expired at now >= expiry (the original query and decision disagreed at equality). This internal scheduling boundary is not an external payment deadline.

## Acceptance

1. SQL keeps the next-action time and claim token/expiry separate from Domain. Received and Uncertain outgoing transactions with no claim and no future due time can be discovered in bounded batches. pacs.008 comes first, then other types, oldest status first; identity breaks equal-time ties. Discovery is advisory, not ownership.
2. Application starts only Received work. A positive fixed claim duration gives one token and changes status to Sending with history atomically. Concurrent starts have one winner. Future-due, missing, already claimed, and other-state work returns no claim.
3. A synchronous decision runs once on a coherent snapshot and performs no external side effects. SQL checks rowversion on the parent before appending history. Declined/no-op decisions persist nothing. Failure/cancellation rolls back claim metadata and history together, including cancellation after the parent write.
4. Completion requires the matching transaction/token and a stored expiry later than the supplied operation time. It commits new status/history, clears ownership, and persists an optional next-action time together. A stale token, expired claim, or losing concurrent writer reports Conflict. Missing identity reports NotFound. No-op decisions report Unchanged and retain ownership.
5. Ordinary unfenced updates cannot write while a claim exists, even after expiry. They report Conflict; recovery or fenced completion is required. This prevents an alternate update path bypassing ownership.
6. Application recovers expired Sending/Investigating/Resending work to Uncertain, source Recovery, with an explanation; it is due immediately and the token is cleared. Competing recovery/completion updates cannot overwrite each other. Uncertain work is discoverable for the later investigation capability, never automatically requeued as Received.
7. A newly created store/context can discover and recover a persisted abandoned claim without any in-memory queue or worker identity. Completing the old token is refused after recovery and after a new claim is acquired.
8. Invalid batch sizes (outside 1..1000), undefined/non-ready query statuses, empty ownership identities/tokens, and nonpositive durations fail before database work. Timestamps are normalized to UTC.
9. No production worker, endpoint, remote call, recovery resend/investigation, heartbeat renewal, or host storage registration is introduced here. The fixed duration models the source's stuck-work threshold; transport capability must choose a duration that exceeds its bounded execution time.

## Verification and limits

Real SQL Server LocalDB tests verify discovery, concurrency, atomic rollback, claim fencing, retry scheduling, cancellation, and reopening persisted work. Apply/rollback/reapply generated migrations and check model consistency. Keep existing architecture, Contracts, lifecycle, intake, and host tests.

This foundation verifies persisted restart recovery with fresh store/context instances, not an operating-system process-kill or network-side-effect experiment. Crash windows around remote processing belong to capability 2/3 and must be tested there. Expiry eligibility is evaluated at the supplied operation time; this is not a database-clock deadline checked at commit. Rowversion and token fencing prevent a completion snapshot from overwriting a recovery that committed first. All workers must use a consistent UTC clock; clock skew and lease duration sizing need deployment validation. Ownership prevents stale database writes, not an already-running remote system from processing a request. No automatic retry of storage decisions is added.

Approved external behavior differences: none.
