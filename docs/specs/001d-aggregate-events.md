# Stage 1: outgoing aggregate and atomic event persistence

Review branch: codex/aggregate-events. Fixed base: e24c28d (transaction work). Owner implementation approval: 2026-10-04. Merge approval pending. Only Stage 1 is implemented in this review.

## Evidence and approved redesign

The copy repository at e24c28d is the implemented lifecycle/intake/ownership reference: Domain/Transactions/PaymentTransaction, Application/Transactions/OutgoingTransactionIntake and OutgoingTransactionWork, Infrastructure/Transactions/SqlTransactionStore and its work partial, and their unit/SQL tests. It was clean at the start of this review. Older review documents identify historical commits; the live capability branch now contains those changes in e24c28d. Do not reset or replay those historical anchors.

Original behavior reference remains d498de6c4638aa71cdb20189d13642b41abab5f1 in the read-only original repository. Source evidence is listed in specs/001a-transaction-lifecycle.md, 001b-durable-intake.md, and 001c-pending-work.md: reference trimming, UTC and reason normalization, description limit, bounded pacs.008 priority, request durability, SQL uniqueness and recovery. Those earlier specifications remain evidence where this specification does not replace them.

Approved internal differences: current state replaces history replay; outgoing operations become guarded business methods; repeated/conflicting final reports preserve the original outcome; only explicit operator resolution changes a final outcome. Incoming-only state CoreUnknown retains its enum value but has no outgoing transition. Incoming processing gets a separate model later.

## Domain acceptance

AggregateRoot owns a nonempty identity, monotonically increasing event sequence, and a read-only pending collection of immutable records. OutgoingPayment is directly materialized by EF through its private constructor without generating events. Stateless 5.20.1 is centrally pinned, private to transition implementation, and absent from public aggregate signatures.

| From | Permitted resulting states |
|---|---|
| Received | Sending, Rejected |
| Sending | Accepted, Rejected, NotSent, Uncertain, Received (confirmed safe connection retry) |
| Uncertain | Investigating, Resending, ManualReview |
| Investigating | Accepted, Rejected, Uncertain, Resending, ManualReview |
| Resending | Accepted, Rejected, Uncertain, ManualReview |
| ManualReview | ManuallyResolved (operator) |
| Accepted, Rejected, NotSent | ManuallyResolved (operator) |

Invalid operations raise PaymentTransitionException without changing state or pending events. Processing observations and repeated final outcomes advance only event sequence. Conflicting final outcomes append a conflict observation. Both retain original outcome time, source, details, and outcome sequence. ManuallyResolved repeats are observations. Explicit operator methods fix the source to Operator.

Keep existing enum numeric values, trimmed references/message types, UTC timestamps, blank reasons/descriptions as null, upper-case reasons, and description truncation at 2000 characters. No EF, HTTP, hosting, signing, or telemetry dependency enters Domain.

## Persistence acceptance

- One scoped TransactionDbContext is shared by a transaction-specific repository, a work repository, and one explicit unit of work. TransactionPersistenceRegistration establishes this lifetime; host registration waits for implemented payment endpoints.
- Load a tracked aggregate, call business methods, stage claim/release/due metadata, then commit. SQL is authoritative for duplicate references, priority discovery, rowversion conflicts, and stored request JSON. No cache.
- Infrastructure shadow properties hold request JSON, direction, claim token, UTC expiry/due time, and required SQL rowversion. Current outcome details are on the parent row; reads need no history.
- A claim is acquired only after its parent update commits. Completion checks identity/token and expiry later than supplied operation time. Recovery is eligible at now >= expiry. Ordinary claimed writes return Conflict; staged ownership and rowversion prevent stale completion/recovery writers. Expiry is evaluated at supplied operation time, preserving the previous foundation's contract; no database-clock commit deadline or renewal is promised.
- The unit of work begins one local SQL transaction. It saves the version-checked parent first, then appends events, then commits. No database transaction spans remote I/O.
- The interceptor only prepares event rows from pending events. It refuses direct business saves and changes/deletions to stored history. The unit refuses caller-created event rows, payment deletions, request replacement, and updates lacking contiguous pending events. Pending events are acknowledged only after commit. Failed units are marked unusable and must be disposed; retained event references are not automatically retried.
- TransactionEvents has unique event IDs and transaction/sequence pairs, stable event names, schema version 1, UTC occurrence time, and complete camel-case JSON payloads with named enum values. No CLR type names are persisted. The explicit registry rejects unregistered event kinds/operations.
- Duplicate intake returns the persisted original even across message types; uniqueness races must not misclassify an unrelated primary-key violation.

## Schema policy

Generate AggregatePaymentsAndEvents with the official EF CLI in Infrastructure. Preserve both historical migrations and support applying/rolling back/reapplying the complete chain on fresh databases. Drop TransactionHistory in favor of TransactionEvents; no old-history conversion exists. Recreate existing disposable rebuild databases. Do not apply this migration to populated or production databases.

## Verification

Aggregate tests exhaust every operation/state combination, event immutability and sequences, normalization, observation semantics, and final-outcome preservation. Real SQL tests cover direct materialization, full payload round trips, intake races, competing claims/observations, expiry and recovery, stale completion, deferred priority discovery, multiple commits without duplicated events, rollback on event failure, cancellation between parent/events, and failed scopes retaining pending events. Run Release build, all tests, formatting, model consistency, contract/dependency checks, and fresh-checkout host verification. Review Standards and Spec independently; owner approval is required before merge.

Stage 2 remains a separate set of reviews described in ../rebuild-plan.md. This refactor adds no payment endpoint, production worker, remote call, signing, XML generation, or artifact schema.

## Owner-approved persistence revision

[001e-shared-unit-of-work.md](001e-shared-unit-of-work.md) supersedes the payment-specific commit interface, its result enum, and responsibility/folder assignments above. All domain, atomicity, ownership and compatibility requirements remain in force.
