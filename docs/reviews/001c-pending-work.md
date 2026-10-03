# Review 1c: durable pending work and abandoned claims

Branch: codex/capability-rebuild. Fixed base: cdd52f9. Local diff is the PR equivalent; merge approval pending.

Problem: intake was durable, but pending work had no durable ownership or restart recovery. This change discovers due outgoing transactions, atomically claims them with history, and recovers abandoned in-flight work to Uncertain. Fenced writes refuse previous owners.

Specification: [001c-pending-work.md](../specs/001c-pending-work.md). Pinned source paths and commit evidence are recorded there.

## Diff

Review with git diff cdd52f9...HEAD and git log cdd52f9..HEAD --oneline.

- Application: transaction-specific work storage interface, ownership value, start/recovery decisions using TimeProvider.
- Infrastructure: nullable scheduling/ownership fields, consistency constraint and indexes, SQL query/claim/completion/recovery operations. Existing atomic persistence is shared rather than duplicated; ordinary updates refuse claimed work.
- Domain, Contracts, and Api remain unchanged.
- EF generated TransactionWorkClaims using: dotnet ef migrations add TransactionWorkClaims --project src/IPS.Middleware.Infrastructure --context TransactionDbContext --output-dir Transactions/Migrations --configuration Release --no-build.
- Generated files: 20261003201355_TransactionWorkClaims.cs, matching Designer, and TransactionDbContextModelSnapshot.cs. Up adds three nullable columns, two indexes, and a paired-field check constraint. Down removes those additions; downgrading would discard scheduling/ownership metadata and is not a production deployment instruction.

## Verification

Release build: zero warnings/errors.
Tests: 96 pass, zero failures/skips (57 unit/architecture/compatibility and 39 integration; 18 new SQL cases).
SQL: competing claims; completion/recovery race; expiry boundary; stale token after recovery and new claim; retry due-time boundary; failure/cancellation rollback; migration apply/rollback/reapply and preserving existing intake data.
Model: no pending changes.
Formatting and diff whitespace: pass.
Fresh checkout of implementation commit 065c0ce: tools/packages restored; Release build, all 96 tests, model consistency, and formatting passed. Working tree stayed clean.
Implementation commit: 065c0ce (based on cdd52f9).

## Standards

No findings in cdd52f9...065c0ce. Application owns start/recovery decisions and TimeProvider; Infrastructure owns scheduling, ownership, SQL persistence, and generated migrations. No actionable code smells were identified. Atomic persistence is shared without introducing generic repositories or placeholder workflows.

## Spec

No actionable findings against acceptance criteria 1–9. Discovery, expiry eligibility, ownership fencing, atomic history/state updates, and recovery to Uncertain match the specification. Scope exclusions and the deferred process-kill/remote-side-effect experiments are explicit.

Both reviewers inspected the diff and source/specification evidence independently and read-only; neither reran tests. The main agent executed the checks above.

Review totals: Standards 0, Spec 0; no outstanding finding on either axis.


## Concerns and scope

A fresh store/context verifies persisted restart recovery; an operating-system process-kill or a remote-side-effect crash test is deferred to sending/reliability. Fencing protects database state; it cannot cancel a remote payment already being processed.

Expiry eligibility uses the supplied operation time, not a database-clock check at commit. Keep clocks consistent and choose a duration exceeding bounded execution time. Rowversion prevents a completion snapshot from overwriting recovery that committed first. No lease renewal, retries of decisions, production polling worker, HTTP endpoints, or protocol operations are introduced.

Approved external behavior differences: none. Explicit token ownership and equal-time tie-breaking are internal redesigns. All work belongs to the copy repository; source and other development branches are preserved.
