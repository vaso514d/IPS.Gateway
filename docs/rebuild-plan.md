# Approved aggregate and synchronous outbound rebuild

Owner approved implementation on 2026-10-04. Execute in the copy repository. Preserve other branches. Each increment has its own specification, meaningful tests, independent Standards/Spec reviews, and owner approval before merge. Source commits are evidence; this branch follows capability order. The active checkpoint in migration-ledger.md governs resuming after compaction or a new session.

## Stage 1: aggregate refactor

Refactor e24c28d into directly EF-mapped aggregates and atomic event history. See specs/001d-aggregate-events.md and ADR 0003. Review this separately before outgoing pacs.008 implementation. Current state is authoritative; full versioned events accompany it and are never replayed to load a payment.

## Stage 2a: outgoing pacs.008 protocol preparation

Inspect pinned source d498de6 before writing the capability specification. Implement request validation, stable identifiers, XML generation, existing-schema validation, signing, and immutable artifact storage. Persist identifiers once and reuse them on retries. Verify XML with independent fixtures; a simulator must not reproduce production mapping logic.

Stage 2a is split for focused review:
- 2a.1: durable identifiers and immutable unsigned/signed artifact storage; see specs/002a1-preparation-storage.md.
- 2a.2: validation, protocol mapping/XML, schema checks, signing, and preparation using that storage.

## Stage 2b: durable workflow

Implement Stage 2b in focused reviews:
- 2b.1: immutable initial-submission marker and raw response, shared ownership/concurrency and SQL verification; see [the specification](specs/002b1-submission-checkpoints.md).
- 2b.2: Application processing and recovery using the stored artifacts/checkpoints, with explicit failure decisions and resume tests; see [the specification](specs/002b2-pacs008-processing.md).

Persist checkpoints and required artifacts:

| Durable checkpoint | Resume |
|---|---|
| Request and identifiers saved | Prepare message |
| XML saved | Sign |
| Signed message saved | Use stored signed message |
| Submission started without durable response | Treat outcome as uncertain; investigate before repeating |
| Raw response saved | Interpret saved response |
| Final outcome saved | Return stored outcome |

Persist the submission marker before remote I/O. A crash between the marker and the call is conservatively uncertain. Database transactions never span remote calls. Ownership acquisition and every checkpoint commit must exclude competing execution and stale results. A lost reply after remote processing must lead to investigation, never a blind resend.

## Stage 2c: host, transport, HTTP and delivery

Owner adaptation (2026-10-05): first implement [2c.0 explicit outgoing message journal](specs/002c0-outgoing-journal.md), keeping the aggregate/Contracts. Separate reviews then add callbacks and synchronous HTTP. Use the owner-supplied outbound flow with separately committed response evidence, pre-send recovery, and service-owned execution.

Run the initial attempt immediately in a service-owned, supervised scope. The HTTP caller awaits the attempt; response completion or caller disconnect must not dispose its execution scope.

- Default HTTP wait: 30 seconds after durable intake.
- Final result, including definitive business rejection: 200.
- Unresolved at deadline: 504 with existing TransactionStatusDto, identity, and current status.
- Duplicate clientReference: immediate 200 with existing current status, including Processing. Never start another attempt or replace request JSON.
- Preserve methods, routes, DTO JSON shapes, status queries, reference comparison semantics, callbacks, and IPS protocol. SQL remains authoritative; no initial cache.
- Approved compatibility exception: replace 202 with the 200/504 rules above. Update route metadata and independent compatibility expectations explicitly in 2c, not Stage 1.
- Store reliable final-status callback work atomically with the outcome even when a synchronous response succeeds. Preserve status-query acknowledgement behavior.
- Recovery workers discover accepted-but-unstarted work and abandoned attempts and invoke the same Application workflow.
- Defaults: IPS call timeout 25 seconds, attempt budget 35 seconds, ownership 45 seconds, outbound concurrency 8. Validate timeout ordering. Caller cancellation stops waiting; processing follows its own deadline and shutdown policy.

Integration tests must cover final 200, unresolved 504, immediate duplicates, caller disconnect, competing request/recovery execution, callbacks, and process termination at each durable checkpoint. Use independent schemas/fixtures and a lost-reply simulator.

## Owner-approved incoming priority — 2026-10-04

After Stage 2b.2, incoming foundations now precede outgoing Stage 2c. Follow [004a](specs/004a-inbound-foundations.md): durable receipt and recoverable ID-only scheduling first; incoming pacs.008 processing second; live receive/processing/response-retry pools third. These are separate reviews. Stage 2c remains deferred with its existing behavior decisions intact.

## Remaining capability order (original; incoming priority above supersedes it)

1. Outgoing reliability: pacs.028 investigation, duplicate-safe resending, deadlines, callback retries, manual review.
2. Incoming payments: separate aggregate/workflow, durable receipt, deduplication, core callbacks, acknowledgement ordering, reconciliation.
3. pacs.009/pacs.004: message-specific behavior and aggregates where justified, demonstrated shared rules.
4. Recalls and payment initiation.
5. Proxy registration/update/removal.
6. Operational completion: readiness, diagnostics, graceful shutdown, multi-instance coordination, measured performance.

Essential recovery and diagnostics accompany the capability needing them. Use SQL Server/EF for real transactions/concurrency; in-memory stores prove application behavior only. No existing-data migration, production cutover, remote creation, package publication, reporting, standalone client/generator/mock hosts, or documentation tooling is included.
