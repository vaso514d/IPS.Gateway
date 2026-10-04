# Stage 2b.1: durable initial-submission evidence

Review branch: codex/pacs008-checkpoints, base 9bd8e4b. Owner authorized the next capability stage on 2026-10-04; merge still requires approval.

## Evidence and scope

Pinned original: d498de6c4638aa71cdb20189d13642b41abab5f1.
- IPS.MiidleWear.API/Transactions/Pacs008TransactionSender.cs, SendAsync: rebuilds and optionally signs XML, queues archive persistence, sends, interprets the reply, then queues audit. It does not durably separate submission from response interpretation.
- IPS.MiidleWear.API/Transactions/OutgoingTransactionDispatcher.cs, ProcessAsync/FinishAsync: claims Received as Sending; cancellation leaves Sending; unknown results become Uncertain. A definite failure to connect can be retried.
- IPS.MiidleWear.API/Transactions/TransactionRecovery.cs, ReleaseStuckAsync/InvestigateAsync: abandoned Sending becomes Uncertain; pacs.008 is investigated before resending; 1016 allows resend, 1017 asks later.
- IPS.MiidleWear.Tests/Api/TransactionRecoveryTests.cs: restart uncertainty, same identifiers on resend after 1016, delayed reinvestigation after 1017.
- Approved rebuild-plan.md Stage 2b supersedes the old archive timing: persist submission before I/O and raw response before interpretation. Missing durable response after submission is uncertain, including a crash immediately before the actual call.

The stage is split into 2b.1 storage evidence and 2b.2 Application processing/recovery. This increment implements the missing storage boundary only. It does not wire a sender, change recovery decisions, parse responses, load certificates, add endpoints or workers, or claim end-to-end crash recovery. Existing request, identifiers, unsigned XML, signed XML and final aggregate outcome are retained.

## Acceptance

1. Add a payment-specific submission repository in Application/Abstractions/Payments with Infrastructure implementation using the existing scoped context and general unit of work.
2. Persist an immutable initial-submission marker (UTC time, owning claim token, selected signed/development-unsigned artifact) and an immutable raw response (HTTP status, exact decoded body, all supplied headers including repeated values). Store explicit camel-case JSON without CLR names in two Infrastructure shadow columns. Keep response data separate from interpreted business outcome.
3. Staging a marker requires persisted pacs.008 in Sending, the current live claim, and the selected artifact already committed. Unsigned development disposition points to UnsignedXml and never populates SignedXml. It is an explicit disposition, not permission to bypass the signing policy: the next workflow must obtain it from the existing host-bound signer and recheck environment permission before dispatch.
4. A second marker is refused even if identical. Only a successful first marker commit authorizes the future workflow's initial remote call. Repository staging never performs I/O. Do not use this single initial marker for later resend attempts; outgoing reliability will model those separately.
5. A response requires the marker already committed and the same current, unexpired claim token that committed that marker. Exact response repetition is a no-op; differing content is rejected. The first observed complete response wins. Empty response bodies and repeated headers are preserved. Headers are a defensive immutable snapshot.
6. Checkpoints are append-only through the repository; direct EF insertion/replacement/clearing or edits after authorization are rejected. Preparation cannot add a different artifact after submission begins. Existing identifiers and artifacts remain immutable.
7. Parent rowversion fences every changed checkpoint. Concurrent marker writers have one winner; recovery or another writer prevents the stale scope from saving a response. Failed scopes are discarded. State, ownership, scheduling, events and checkpoint changes can be committed atomically; metadata alone adds no business event.
8. Generate the migration with the official EF CLI, preserve historical files, verify the full fresh-database chain and model consistency. SQL constraints require JSON, a marker before response, pacs.008 identifiers and a stored unsigned artifact before submission. Interceptor/repository rules enforce selected artifact and exact authorized content. Existing disposable databases must be recreated; no production migration.

## Verification and next increment

Real SQL tests cover prerequisites, marker single-use, precommit visibility, exact round trips, unsigned disposition, response snapshot immutability, unauthorized writes, competing writers, expiry/recovery fencing, atomic outcome/response/ownership save, and rollback/cancellation retaining pending events. Existing behavior tests run alongside the new suite. The fresh-database migration test compares the applied chain with all discovered migrations rather than a hard-coded count of four.

Stage 2b.2 must specify and implement the workflow using these durable facts: no marker means resume preparation; marker without response means uncertainty/investigation; response means interpret without sending; final outcome means return stored state. It must also integrate stored request validation/mapping, configuration policy, safe preparation recovery, cancellation and diagnostic outcomes. Recovery currently conservatively treats abandoned Sending as uncertain and is deliberately unchanged here. Stage 2c owns actual HTTP transport, supervised execution, caller deadlines, and atomic callback delivery work. No database transaction may span remote calls.
