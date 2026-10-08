# Stage 1 review: aggregate and event persistence

Status: Ready for owner review; merge approval pending.
Review branch: codex/aggregate-events.
Fixed base: e24c28d (transaction work).
Implementation: 3bc9b3d.
Review fixes: 5a53658.
Diff: git diff e24c28d...5a53658.
Specification: ../specs/001d-aggregate-events.md.
Approved stages: ../rebuild-plan.md.

## Change

OutgoingPayment directly persists authoritative current state and raises immutable domain events. Private Stateless 5.20.1 configuration replaces unrestricted status changes with business methods. Repeated/conflicting final replies become observations retaining the original outcome; explicit operator resolution is the exception.

Transaction/work repositories and one explicit unit of work share a scoped EF context. Infrastructure retains request JSON, claims, scheduling and rowversion as metadata. The version-checked parent saves before event rows in one local SQL transaction. An interceptor prepares complete JSON event records, and pending events are acknowledged after commit. Failed scopes retain events and cannot be reused.

The host and imported Contracts sources/baselines remain unchanged. No payment endpoints or workers are introduced. Architecture allows Stateless in Domain, including its resolved dependency in Application, while rejecting other external core dependencies.

## Source evidence

Base e24c28d supplies the implemented intake/ownership guarantees being refactored. Original behavior is pinned at d498de6c4638aa71cdb20189d13642b41abab5f1. Relevant source paths and history are recorded in specifications 001a/001b/001c and summarized in 001d. Live Git state was clean at e24c28d; earlier review anchors are historical evidence rather than the current base.

Stateless external state storage uses the property getter/setter model documented in [the official documentation](https://github.com/dotnet-state-machine/stateless#external-state-storage). The exact [5.20.1 package](https://www.nuget.org/packages/Stateless/5.20.1) is centrally pinned. Library types do not appear in public aggregate interfaces.

## Verification

Native Windows .NET SDK 10.0.401, repository-local EF CLI 10.0.12, SQL Server LocalDB MSSQLLocalDB. Checks pass at 5a53658:

- Fresh independent temporary clone: tool restore, solution restore, Release build (zero warnings/errors), all tests, formatting, EF pending-model check, and clean Git status.
- 178 tests: 148 unit/architecture/contract cases; 30 integration cases (23 real SQL, 7 host).
- All outgoing operation/state combinations, immutable events/sequences, UTC/reason/description normalization, repeated/conflicting final outcomes, and technical observations.
- SQL current-state materialization with no events/replay; full immutable JSON event round trips; repeated commits without duplicates; intake uniqueness races; parent-first competing writes; claim fencing; recovery from Sending/Investigating/Resending; due-time and priority discovery.
- Rollback for intake, claim, completion and recovery event failures; cancellation between parent and event writes; retained pending events and refusal of failed-scope reuse.
- Complete generated migration chain apply/down-to-zero/reapply on fresh databases; no pending EF model changes.
- Imported Contracts sources/baselines and historical migration files have no diff against e24c28d.
- Actual fresh-checkout Kestrel host starts, liveness returns 200 Healthy, development OpenAPI paths contain only /health/live.
- git diff --check passes.

Generated migration command:

```powershell
dotnet ef migrations add AggregatePaymentsAndEvents --project src/IPS.Middleware.Infrastructure --context TransactionDbContext --output-dir Transactions/Migrations --configuration Release
```

Generated files: 20261003212221_AggregatePaymentsAndEvents.cs, its Designer, and TransactionDbContextModelSnapshot.cs. The initial scaffold incorrectly made shadow rowversion nullable; the model was corrected and the new migration removed/regenerated through EF CLI. Both historical migrations remain unchanged.

## Standards

Initial independent review found no substantive standard breach and one low-severity possible Mysterious Name: Core_projects_depend_only_on_the_base_class_library permitted Application's resolved Stateless assembly. Renamed to Contracts_and_Application_declare_no_external_packages in 5a53658.

Final independent disposition: **no actionable findings**. The rename resolves the naming issue. Added SQL cases follow the documented requirement to test transaction guarantees against SQL Server. No residual documented-standard breaches or actionable Fowler smells found. Reviewer inspected the diff read-only; test execution was performed by the implementing agent.

## Spec

Initial independent review found P2 retained coverage gaps: recovery tests covered only Sending, and failed completion/recovery rollback cases were missing. Commit 5a53658 restores all three in-flight recovery states and verifies failed release preserves persisted state, event sequence, token, expiry, scheduling and history, retains pending events, and rejects failed-scope reuse.

Final independent disposition: **no remaining findings**. No production behavior changed in the fix. Stage 2 remains gated and owner merge approval remains required. Reviewer independently inspected fixes read-only; test execution was performed by the implementing agent.

Final review counts: Standards 0; Spec 0.

## Limits and next gate

AggregatePaymentsAndEvents drops TransactionHistory without data conversion. The entire chain is supported on fresh databases only; recreate existing disposable rebuild databases. No populated or production database was migrated.

Ownership expiry remains evaluated at supplied operation time, not database-clock commit time. No heartbeat renewal or remote execution guarantee exists yet. SQL reopening tests use fresh contexts, not process termination around remote I/O. Those tests and supervised synchronous processing belong to Stage 2b/2c.

Await owner approval before merging into codex/capability-rebuild. After approval to advance, establish Stage 2a protocol-preparation specification from pinned source before implementing it. Preserve all other development branches and the read-only original repository.
