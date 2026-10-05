# 005b: Typed persistence, repositories and unit of work

Base: 95ca779 on codex/readability-refactor. Implements increment 2 of 005. No merge authorized.

Replaces payment shadow metadata and EF.Property queries with typed Infrastructure companions. Keeps Domain aggregates mapped to current state, a shared SQL rowversion, frozen requests/artifacts, ownership checks and deterministic discovery. Explicit typed guards replace the incoming interceptor's property-name rule arrays. Save state is separate from DbContext mapping; EF value snapshots replace JSON comparisons for authorizing evidence. Parent writes precede deferred evidence/events and event acknowledgement remains after commit. The general unit of work also saves ordinary entities.

Official generation: `dotnet ef migrations add TypedPaymentMetadata --project src/IPS.Middleware.Infrastructure --configuration Release --output-dir Transactions/Migrations`. Generated migration 20261005181308 changes discovery indexes only. All column nullability, check constraints and historical migrations remain intact; supported on fresh disposable rebuild databases only. The required typed navigation is used so EF-generated snapshots reproduce the model.

Existing crash/tamper tests now access typed metadata without changing expected behavior. Added regressions for required same-row mappings and defensive authorization snapshots of rowversion bytes.

Independent Standards review: clear; its nonblocking WriteOnce naming suggestion was applied as WriteUnsignedXml. Independent Spec review: clear, including the final generated migration, shared-row fence, ordering, JSON and event compatibility. Final verification: 876 tests pass (257 unit/architecture/Contracts and 619 integration), zero failures/skips, including LocalDB, process recovery and host checks. Release build is clean; formatting, whitespace and EF migration-model checks pass. Results: readability-persistence.trx in both test projects.
