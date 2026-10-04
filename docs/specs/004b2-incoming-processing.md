# Incoming pacs.008 durable processing

Specification for codex/incoming-processing, stacked on protocol commit ab6c4f8. 004b.2a is implemented and awaiting review (see [Implemented: 004b.2a](#implemented-004b2a)). The owner approved the protocol merge; ab6c4f8 is now in codex/capability-rebuild, recorded by checkpoint 7bb0452. This document prepares the next implementation; it does not enable live clients, workers or endpoints.

## Decisions and evidence

Use the pinned source evidence in [004b](004b-incoming-pacs008.md), especially CoreReferences, InboundCoreCaller, InboundCoreReconciliation and their tests at d498de6c4638aa71cdb20189d13642b41abab5f1. Protocol reading/mapping/replies are implemented by 004b.1; receipt deduplication and scheduling by 004a.

Owner decisions on 2026-10-04:

- Business identity is normalized receiving participant BIC plus exact EndToEndId. Keep the external CBS idempotency/status key equal to EndToEndId, unchanged. Identical frozen payment contents reuse the existing payment; conflicting contents are held for investigation without changing the canonical payment or calling CBS again.
- IPS deadline is acceptance time, with receipt time as fallback, plus 20 seconds. Reserve up to 3 seconds for an inline CBS status query and 2 seconds for reply preparation/delivery. Shorten the initial CBS call to the remaining budget. Already-expired work never initiates a CBS submission.
- Unknown CBS outcome at the deadline produces an immutable RJCT/MS03 decision and durable reconciliation. A later confirmed credit requires a reversal request. Successful reversal-request delivery confirms acceptance only, not completed reversal; continue reconciliation or manual review.

The same EndToEndId can exist under different participants internally, while CBS receives an unchanged key. Live integration must establish how CBS scopes that key; do not assume a global or participant-scoped external guarantee from our SQL uniqueness rule.

## Keep reviews small

Implement this roadmap as separate focused increments, with tests and owner merge approval for each:

1. **004b.2a: identity, aggregate and persistence.** IncomingPayment current state/events, payment identity and immutable snapshot, journal attachment/conflict disposition, payment ownership and repository operations, and shared atomic persistence. No remote workflow execution.
2. **004b.2b: CBS processing and recovery.** Application workflow through CBS interfaces, durable submission/response checkpoints, status-first recovery, remaining-time budgets, and separate CBS outcome/IPS decision. Test with independent simulators, not live clients.
3. **004b.2c: reply artifacts and reconciliation.** Persist reply context and exact prepared message, durable delivery work, late-credit reversal work and its uncertain/accepted/completed distinctions. Crash/retry/lease tests precede live integration.

Only after these reviews implement the receive, processing and response/retry pools and production client/configuration wiring. Further split a review if its diff is too large to assess coherently.

## 004b.2a: identity and aggregate specification

Use a separate IncomingPayment aggregate with guarded business methods and immutable versioned events. Keep CBS outcome, IPS reply decision and reconciliation state distinct; an IPS rejection never proves a CBS rejection. Domain contains no XML, EF, HTTP or Contracts types. Loading current state raises no events and does not replay history.

Freeze the mapped core-request values before creating business identity. Compare all fields of the internal payment snapshot structurally, including nulls, exact strings and ordered nested collections; record the comparison format/version. Do not use raw signed XML, transport sequence, envelope IDs or a digest alone as equality. Do not silently normalize EndToEndId or apply outgoing validation. A changed frozen field is a conflict. Original protocol references remain attached to each receipt for correlation, separate from the canonical business snapshot.

SQL enforces unique normalized receiving participant plus ordinal EndToEndId, with explicit comparison semantics preserving trailing characters rather than relying on SQL padded string equality. Match this in application lookup and tests. Preserve the receipt's immutable payload. Concurrent identical registrations converge on one payment; losing uniqueness/concurrency attempts discard their scoped context and retry boundedly from committed state.

Multiple journal entries can reference one payment. Journal leases alone cannot prevent two distinct sequences from calling CBS for that payment: introduce payment-level ownership with token, expiry and rowversion in Infrastructure metadata. Require committed ownership before processing, and fence each persisted result. Do not hold SQL transactions across external I/O. A competing or expired owner cannot commit checkpoints; expiry permits investigation/resumption, never unconditional reposting.

Extend the journal disposition constraint deliberately: missing/nonpositive sequence stays Held; trusted-read failures and identity conflicts can also become Held with a diagnostic reason. Held work stays excluded from scheduling. A trusted FF01 read result requires a durable receipt-scoped reply path without creating a fictitious valid payment; implement its artifact/delivery path in 004b.2c. Do not mark a receipt complete merely because another receipt references its payment.

Application repository interfaces belong under Abstractions/Inbound; implementations under Infrastructure/Repositories/Inbound. Keep concrete feature workflows and a shared IUnitOfWork. Stage journal attachment/disposition, payment state, metadata and events in the same context and commit atomically. No generic repository or second unit of work.

### Event history constraint

Superseded by the owner's 004b.2a plan: events reference the shared AggregateIdentities table through typed foreign keys. See [Implemented: 004b.2a](#implemented-004b2a). Original draft for the record:

Current TransactionEvents.TransactionId references OutgoingPayment specifically. The interceptor can observe AggregateRoot, but its SQL mapping does not yet support incoming aggregates. Preserve the one append-only TransactionEvents table and existing event registry/sequence rules. The schema change must represent exactly one valid outgoing or incoming owner and retain database-enforced referential integrity; do not simply drop the outgoing foreign key and leave events unowned. A proposed implementation is separate nullable outgoing/incoming owner foreign keys with a check that exactly one matches TransactionId. Review generated migration and full model against both aggregates before accepting this design. Domain event public interfaces must remain independent of that SQL representation.

Generate the migration with the official EF CLI. Preserve historical migrations and verify the complete chain against a fresh SQL database; no production conversion or data migration is promised.

## Subsequent workflow checkpoints

| Committed evidence | Allowed resume action |
|---|---|
| Canonical snapshot and receipt correlation | Acquire payment ownership, then prepare a first attempt within remaining budget |
| CBS submission marker, no stored response | Query CBS status; never blindly submit again |
| Complete stored CBS response | Interpret the stored response |
| CBS final outcome | Reuse that outcome; do not repeat submission |
| IPS decision and reply context | Prepare the response using stored identifiers/time |
| Prepared reply and delivery work | Send/retry the exact stored message |
| Rejection with unknown CBS outcome | Reconcile independently; never replace the rejection |
| Late credit after rejection | Persist reversal work before delivery |
| Reversal request accepted | Await authoritative completion evidence or manual review |

A first attempt uses max(0, deadline - now - 5 seconds). Inline status uses at most min(3 seconds, deadline - now - 2 seconds), with cancellation at the allocated deadline. Recompute remaining time before each action. No available submission budget means no new submission. Define status-query scheduling and already-expired rejection preparation in 004b.2b tests; stored final outcomes take precedence over retrying effects. Reply margin is a budget, not a guarantee of remote delivery.

Distinguish confirmed CBS RJCT from unresolved timeout, malformed/unmatched data and PDNG. Preserve the source difference between inline 404 (unknown) and reconciliation 404 after IPS rejection; verify the CBS contract before enabling the latter as authoritative evidence. No simulator may invent reversal-completion semantics absent from the external contract.

## Acceptance and review gates

For 004b.2a, verify directly against SQL Server:

- Same participant/reference and equal contents across different sequences creates one canonical payment; conflicts retain both receipts but cannot overwrite it.
- Participant scope, exact reference comparison, case/trailing characters, nulls and ordered nested values agree between lookups, uniqueness and equality.
- Competing receipts cannot both acquire payment ownership; expiry/reacquisition fences stale mutations.
- State, journal attachment/disposition and complete versioned events roll back together, including cancellation and failed commit.
- Both incoming and outgoing event ownership enforce foreign keys; orphan and ambiguous owners fail. Aggregate materialization, sequences, multiple saves and failed-event acknowledgement retain existing guarantees.
- Held entries remain excluded from discovery; existing incoming receipt, outgoing processing and Contracts tests remain green.

Later slices add process termination at every checkpoint, a simulator that credits before losing the reply, no blind resubmission, injected-time deadline boundaries, immutable signed-response replay, and accepted-but-unconfirmed reversal recovery.

Each implementation review must include build, formatting, architecture/Contracts, generated model/migration consistency, full SQL tests and independent Standards/Spec reviews. Merge only after owner approval. The ab6c4f8 protocol slice was verified with 416 passing tests. 004b.2a verification is recorded in its [review evidence](../reviews/004b2a-incoming-identity.md).

## Implemented: 004b.2a

Owner instruction on 2026-10-04: implement identity, aggregate and persistence from 9f9af8b, adding only registration-related aggregate behavior. [Review evidence](../reviews/004b2a-incoming-identity.md).

- **Aggregate.** IncomingPayment.Register trims and uppercases the participant BIC, keeps EndToEndId exactly as read, converts the time to UTC and raises one IncomingPaymentRegistered event (schema version 1, name incoming-payment.registered). There are no CBS, reply or reconciliation states yet.
- **Snapshot and comparison (format version 1).** RequestJson stores `{version: 1, value: Pacs008Request}`. Restoration freezes nested lists into read-only lists with ordered value equality and never runs outgoing validation. IncomingPacs008.HasSameContents compares the frozen requests with record equality:
  - strings compare ordinally;
  - decimals compare numerically;
  - DateTimeOffset values compare by UTC instant;
  - lists compare by ordered contents, and null differs from empty.

  Transport sequence, signature and envelope identifiers are not part of the request, so they never affect the comparison.
- **Registration.** IncomingPaymentIntake.RegisterAsync(claim, incoming) looks up the payment from the live owner's receipt participant and the EndToEndId.
  - No payment exists: create one, attach the receipt and return Created.
  - Equal contents: attach the receipt to the canonical payment and return Existing. Its event history, schedule and ownership are untouched.
  - Different contents: save its trusted original references and hold the receipt with ConflictReason atomically, without attaching it; return Conflict with the canonical payment ID.
  - The claim is no longer live: return LostOwnership without staging anything.

  Trusted original references are staged under receipt ownership before the registration decision is committed. They are stored independently of payment attachment; identical repeats are allowed and replacements throw. ReadOriginalReferencesAsync returns them for held or attached receipts. Attachment requires saved references, never marks a receipt processed, and cannot be moved to another payment.
- **Races.** Uniqueness and rowversion failures propagate and fail their scope. IncomingPaymentRegistration retries up to eight times in fresh scopes through the FreshScopeRetry helper shared with receipt registration. A later attempt reuses only a committed winner whose contents match.
- **Journal.** IncomingPaymentId and OriginalJson are write-once and need journal ownership; references may exist without an attachment, but an attachment requires non-null valid reference JSON. Any non-null reference JSON must be valid. HoldReason is also write-once under ownership. CK_InboundJournal_Sequence now permits Held for any sequence and still requires a positive sequence for Pending or Processed. InboundWork.HoldAsync holds protocol failures. Held rows have no claim or schedule.
- **Payment ownership.** Claim token, expiry, next action and rowversion are shadow metadata. A new payment is due at its registration time. AcquireAsync commits before it returns a claim and uses the caller's duration, normally the 45-second scheduling default. ReleaseAsync needs the exact live token and reschedules. A stale loaded row fails at commit and returns false. FindDueAsync orders by next action, registration time and ID, takes at most the requested number, and excludes live claims. No worker or channel is registered.
- **Shared identity and events.** AggregateIdentities (Id, Kind) uses kinds outgoing-payment and incoming-payment.
  - Transactions and IncomingPayments each carry a persisted computed kind column and reference the identity through a composite foreign key.
  - TransactionEvents stores AggregateKind and references the identity through a composite foreign key that replaces the old foreign key to Transactions.
  - CK_TransactionEvents_Kind requires `payment.*` names for outgoing aggregates and `incoming-payment.*` names for incoming ones.
  - The interceptor adds identity rows for new aggregates in the entity phase. Identity and event rows are append-only and cannot be staged directly.
- **SQL identity.** The unique index is on (ParticipantBic, EndToEndId, EndToEndIdBytes): both text columns use Latin1_General_100_BIN2 and EndToEndIdBytes is the persisted DATALENGTH. SQL Server pads trailing spaces even in binary collations, and pads trailing zero bytes when comparing varbinary, so a plain binary key is not exact. Adding the byte length makes uniqueness match ordinal comparison, including case and trailing characters. Lookup filters in SQL, then chooses the ordinal match.
- **Integrity limits.** SQL guarantees that every event and state row belongs to an identity of its own kind. It does not guarantee that an identity has a state row: only the interceptor writes the two together, and a raw SQL insert can leave an identity without state. Conflict-held receipts have no attachment but retain their write-once trusted original references. No reply or acknowledgement is enabled by this storage change.
- **Migration.** 20261004154416_IncomingPaymentIdentity was generated by the repository EF CLI. Only line endings and BOMs were normalized to match the repository conventions. It supports fresh databases only and converts no data.
- **Correlation correction.** 20261004162057_InboundReceiptReferences was generated with EF CLI 10.0.12. It only replaces CK_InboundJournal_Attachment; prior migrations are preserved. The new SQL check explicitly rejects null references on an attached receipt, avoiding SQL CHECK acceptance of UNKNOWN.
- **Out of scope.** CBS calls, FF01 reply artifacts, reconciliation, workers, endpoints and Contracts are unchanged.
