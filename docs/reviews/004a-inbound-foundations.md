# Incoming foundations review

Branch codex/inbound-foundations, base 5348f78 on codex/capability-rebuild. The owner approved commit and merge into codex/capability-rebuild on 2026-10-04, including the recorded cleanup. Pre-merge verification reran successfully: 394 tests, Release build, formatting and EF model consistency.

## Result and diff guide

A received envelope can be stored durably, deduplicated and scheduled by ID without enabling IPS polling or payment processing. Invalid sequences are held as approved. [Specification and pinned source evidence](../specs/004a-inbound-foundations.md).

- Application/Inbound: immutable normalized receipt input and detached results; concrete intake and work operations with explicit commits. Abstractions/Inbound separates receipt access from work discovery/ownership.
- Infrastructure/Repositories/Inbound and Persistence: journal mapping, immutable original data, staged duplicate increments and claim/completion/release; SQL binary identity comparison, positive-sequence filtered unique index, check constraints and rowversion. Existing scoped context and shared UnitOfWork are reused unchanged in responsibility.
- Infrastructure/Inbound: bounded nonblocking ID channel; fresh-scope registration retries with commit-before-notification; bounded SQL refill. AddInboundFoundations registers services without activating hosted workers or altering Api.
- Generated migration 20261004142701_InboundMessageJournal adds only the journal table/constraints/indexes. Historical migrations are preserved; Down drops only the new table. Complete migration chain remains for fresh/disposable databases.

## Verification

- Release build: zero warnings/errors.
- Final complete suite: 394 passed, zero failed/skipped (207 unit/architecture/Contracts and 187 integration). LocalDB started successfully for this review; the previous outgoing review's SQL environment blocker no longer applies to these results.
- New coverage: four Application envelope cases and nineteen inbound integration cases, including real SQL insert/duplicate races, canonical payload preservation, participant isolation, Held invalid sequences, competing and expired claims, stale completion, ordered/excluded discovery, queue saturation/coalescing and restart recovery, cross-feature atomic commits/rollback, cancelled/failed registration, cancellation after commit before notification, completed duplicates and immutable receipt data.
- Existing host, outgoing processing, signing, architecture, Contracts and full migration-chain recreate tests pass. Java JSR105 fixtures ran with JDK20.
- Formatting verification, diff whitespace and EF pending-model checks pass. No new packages or Contracts changes.
- Official generation: `dotnet ef migrations add InboundMessageJournal --project src/IPS.Middleware.Infrastructure --context TransactionDbContext --output-dir Transactions/Migrations --configuration Release --no-build` using repository EF CLI 10.0.12. Generated output formatted with dotnet format; no handwritten migration artifacts.

## Standards

Independent read-only review against 5348f78 and all new files: zero documented violations and zero actionable design smells. Layer boundaries, shared UnitOfWork responsibilities, dedicated journal interceptor, staged writes, fresh failed scopes and injected time were confirmed. No speculative generic framework or placeholder worker was introduced.

## Spec

Independent read-only review against 5348f78 and all new files: zero actionable findings. Durable receipt, sequence identity, invalid-sequence holding, rowversion fencing, notification after commit and SQL recovery match the approved scope. Three additional test-only edge cases were added and passed after the reviews: post-commit cancellation, completed duplicate receipt, and discovery tie ordering.

## Owner-requested cleanup — 2026-10-04

Behavior-preserving, no test changed. Discovery and acquisition share one due/unowned predicate (acquisition filters in SQL instead of re-checking in memory); InboundWork commits through one helper and no longer repeats repository validation or UTC conversion; completion loads through FindAsync; the held/deduplication decision is computed once; registration names its eight-attempt bound; discovery counts notifications directly; the channel uses default bounded options and the .NET Lock type. Afterwards: Release build zero warnings/errors, 394 tests pass (after restarting a LocalDB instance that failed with error 50), format, whitespace and EF model checks pass.

## Operational limits and next review

Ownership expiry is checked at staging, with rowversion checked at commit, matching existing ownership semantics. Duplicate metadata updates may invalidate a loaded owner's rowversion; discard and reload after a conflict. Never use scope recovery as permission to repeat remote effects. Registration retries at most eight uniqueness/concurrency conflicts in fresh scopes; exhaustion returns failure and publishes nothing. Channel contents and coalescing are process-local optimizations only.

The journal does not prove business-payment deduplication across different IPS sequences. No incoming payment aggregate, live worker, parser, transport, CBS call, acknowledgement or response channel exists yet. Next, after review/merge approval, specify incoming pacs.008 processing, then live receive/processing/response-retry integration. Preserve CBS uncertainty and durable acknowledgement/reply rules in those later specifications.
