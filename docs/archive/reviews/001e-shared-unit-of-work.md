# Review: shared unit of work revision

Status: reviewed; owner authorized a local commit on codex/aggregate-events. Merge approval pending.
Implementation base: e24c28d. Historical comparison: bc17a81 (previous Stage 1 implementation).
Specification: ../specs/001e-shared-unit-of-work.md.

The prior Stage 1 implementation was present as uncommitted changes when this revision began. Implementation preserved that work without moving branches or merging. After verification, the owner requested committing the complete reviewed changes. Reviewers examined the working tree, including new untracked paths; a tracked-only git diff omits new files.

## Change

IUnitOfWork.SaveAsync saves every tracked change through the shared EF context. The unit orchestrates an explicit local transaction, saves entities before events, translates general concurrency/uniqueness failures, and acknowledges AggregateRoot event snapshots only after commit.

PaymentPersistenceInterceptor owns payment deletion, ownership, and immutable request checks. DomainEventsInterceptor owns sequence validation, append-only history, and preparing event rows. Intake interprets a unique failure as a duplicate reference only when SQL contains a reference winner; otherwise it rethrows the original general uniqueness error.

Repository interfaces and implementations now live in Application/Repositories/Payments and Infrastructure/Repositories/Payments. Persistence abstractions, configurations, events, and interceptors have dedicated folders. Application workflow result/claim/history models remain with Transactions.

The context retains its historical TransactionDbContext CLR identity for EF migration discovery. Its file now lives under Persistence and it saves ordinary entities as well as payment aggregates. No schema, migration, Contracts, Domain behavior, or host exposure change was introduced by this revision.

## Verification

- 185 tests pass: 149 unit/architecture/contract cases, 36 integration cases (29 SQL and 7 host).
- Release build: zero warnings/errors.
- Clean temporary source export including current untracked files: tool restore, solution restore, build, all tests, format verification and EF pending-model check pass.
- New SQL cases prove ordinary insert/update/delete, mixed ordinary/aggregate commits and rollback, rollback of other tracked changes after stale aggregate writes, general uniqueness failures, and shared scoped registration.
- New application case proves unrelated unique failures propagate when there is no reference winner.
- Existing SQL guarantees retain intake idempotency, ownership fencing, competing writes, recovery, scheduling, rollback/cancellation, complete event payloads, and commit acknowledgement.
- EF reports no pending model changes. Historical migration files and Contracts have no diff from bc17a81.
- git diff --check passes.

## Standards

Independent read-only review: **no actionable findings**. The revision follows approved responsibility separation and requested folders. The shared abstraction has implemented uses and meaningful ordinary-entity SQL tests. The retained context identity follows the documented migration constraint. No documented breaches or actionable Fowler smells found.

## Spec

Independent read-only review: **no findings** against 001e and retained 001d invariants. All tracked changes share the transaction; entity writes precede events; snapshots are acknowledged after commit; failed scopes cannot be reused. Intake and work workflows handle general persistence exceptions as specified. No scope creep found.

Final counts: Standards 0; Spec 0.

## Next gate

The owner authorized a local commit of revised Stage 1. No merge is authorized. Stage 2a remains next after approval to advance. Existing Stage 1 schema policy still requires fresh disposable databases; this revision adds no further schema migration.
