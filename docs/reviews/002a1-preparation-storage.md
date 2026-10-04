# 2a.1 review: stable identifiers and immutable preparation storage

Status: implemented and verified; owner review/merge approval pending. Changes are uncommitted on codex/pacs008-preparation, based on b18938b. Stage 1 was merged into codex/capability-rebuild with owner approval on 2026-10-04. main and the original repository are unchanged.

Specification and source evidence: [002a1-preparation-storage](../specs/002a1-preparation-storage.md).
Review scope: git diff b18938b plus every untracked file from git ls-files --others --exclude-standard. Inspect new files as well as the tracked diff.

## Result and diff

- Application: preparation snapshot and payment-specific repository interface; the owner-approved revision now validates the intake envelope at Application entry and passes a validated type into intake. No inline intake guards.
- Infrastructure: generate two protocol IDs atomically with pacs.008 intake; read/stage unsigned and signed XML using the shared context. Exact-value authorization, current-claim checks, immutable committed values and SQL rowversion protect writes.
- Persistence: four nullable shadow columns and two filtered unique indexes on Transactions; consistency constraint; metadata-only saves permitted without inventing Domain events. Business property modifications still require pending events.
- Tests: 22 new SQL cases; DI test also exercises the new repository; complete migration chain test updated.
- Documentation: focused scope, original-source evidence, merge decision and resume checkpoint.

The storage layer preserves exact text; these tests use simple independent storage fixtures, not ISO-valid or cryptographically signed messages. Protocol validation and cryptography are the next review.

## Verification before the validation ownership revision

207 tests passed, zero failed/skipped: 149 unit/application/architecture/Contracts and 58 integration (51 real SQL, 7 host). Release build: zero warnings/errors. Formatting and git diff --check passed. EF reports no pending model changes.

Fresh source export, including untracked files and excluding ignored build outputs:
C:\Users\omo\AppData\Local\Temp\ips-preparation-check-c12107d49c12456aafbf07bec32212fd

That export passed tool restore, solution restore, Release build, the same 207 tests, formatting, and EF model consistency. Host startup/liveness remains covered by the host integration suite. All SQL tests use disposable IPS_Middleware_Tests_* LocalDB databases, including complete migration rollback to zero and reapply. No application database was used.

Migration generated with the repository-local official EF CLI 10.0.12:

    dotnet ef migrations add Pacs008PreparationStorage --project src/IPS.Middleware.Infrastructure --context TransactionDbContext --output-dir Transactions/Migrations --configuration Release --no-build

Generated files: 20261003220716_Pacs008PreparationStorage.cs, its Designer, and the model snapshot. Up only adds four columns, two indexes and a check constraint. Down removes them. Historical migrations were not edited.

Initial verification exposed missing early guards in the committed intake: existing cancellation/blank-payload tests hung on the fake save. At that checkpoint guards were restored and the suite rerun. The later owner decision supersedes that implementation: entry validation now owns input checks and the cancellation fake honors tokens. A DI test setup omission was also corrected before the final passing run.

## Standards — before the validation ownership revision

Final independent disposition: no actionable findings. Exact artifact authorization remains in Infrastructure; ownership checks are shared between demonstrated callers; no generic storage framework was added. The regression cases and early intake guards follow repository conventions. Reviewer inspected changes read-only and did not rerun SQL.

## Spec — before the validation ownership revision

Initial finding: P2, direct EF edits could change an authorized first artifact before commit. Resolved by retaining the exact authorized value and verifying ordinal equality, with four SQL regressions for empty/changed unsigned and signed values.

Final independent disposition: no remaining findings. The fix covers the first-write bypass; intake guards retain the documented existing behavior; scope and checkpoints correctly defer 2a.2. Reviewer inspected changes read-only and did not rerun SQL.

Summary: Standards 0 findings; Spec 0 remaining findings (1 corrected).

## Limits and next review

- Artifact fields reside on the parent row and are loaded with a tracked aggregate. Revisit projections/storage only with measured access patterns.
- Expiry is checked at staging time; this is not a database-clock deadline enforced at commit.
- Populated disposable rebuild databases must be recreated. No identifier backfill or production migration is supplied.
- XML generation, XSD validation, signatures, submission markers, transport, recovery orchestration, payment endpoints and callbacks are not implemented here.
- After owner approval, merge this slice into codex/capability-rebuild and establish the 2a.2 specification from pinned source. Do not silently carry forward unsigned-send or C14N assumptions.


## Validation ownership revision — 2026-10-04

Owner explicitly kept inline intake guards removed and approved Application entry validation. Preserved owner cleanup: interfaces moved into Application/Abstractions/Payments, IUnitOfWork comment removal, and explicit parentheses in the aggregate condition. The current spec supersedes the historical repository-interface folder decision.

ValidatedIntakeRequest is privately constructed with read-only properties. Its factory returns structured errors and checks only the existing foundation envelope (required values and SQL identifier lengths); full payment validation remains in 2a.2. Intake accepts the validated type without repeating checks. Tests moved blank-input cases to the validator and corrected cancellation semantics in the fake.

Current verification: 216 passing tests (158 unit/application/architecture/Contracts; 58 integration including 51 SQL and 7 host), Release build zero warnings/errors, formatting clean, no EF model changes, diff whitespace check clean. The earlier fresh-export run above predates this Application revision.

Re-review results: Standards 0 findings; Spec 0 functional findings. The Spec reviewer requested correction of the older inline-guard claims; the result and historical labels above now distinguish the superseded implementation from the current revision. Reviewers inspected code/tests read-only and did not rerun SQL. All changes remain uncommitted on codex/pacs008-preparation.