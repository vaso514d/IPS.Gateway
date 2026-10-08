# Stage 2b.1 review: durable submission evidence

Branch codex/pacs008-checkpoints; base 9bd8e4b. Owner authorized committing this increment with their cleanup. It remains unmerged; owner approval is required before merge.

## Result and diff guide

The missing durable evidence between prepared XML and a business outcome is now stored atomically with the existing scoped unit of work. A future sender can distinguish no submission marker, submission with unknown outcome, and a stored response awaiting interpretation. This increment does not implement that workflow or change current recovery decisions.

- Application/Abstractions/Payments/IPaymentSubmissionRepository.cs: explicit stage/read boundary, no callbacks or remote I/O.
- Application/Payments/Pacs008/PaymentSubmission.cs and IpsSubmissionResponse.cs: marker, signed/development-unsigned disposition and defensively copied response headers. Body and headers retain exact supplied text and repeated entries.
- Infrastructure/Repositories/Payments/PaymentSubmissionRepository.cs: committed-artifact ordering, single-use marker, original live claim, immutable response and parent rowversion fencing.
- Infrastructure/Persistence/PaymentArtifacts.cs: the owned-pacs.008 guard and write-once slot staging, now shared by preparation and submission instead of duplicated in each repository. Preparation freezes after submission. Shared UnitOfWork and Domain remain unchanged.
- Generated migration 20261004125120_Pacs008SubmissionEvidence adds two nullable JSON artifact columns and a structural check constraint. Historical migration files are unchanged; the snapshot is generated. Up is additive; Down removes these new artifacts. Full migration chain is supported on fresh databases only.
- 28 SQL cases and one unit case cover ordering, immutable snapshots, commit visibility, signed/unsigned storage, competing writers, expiry/recovery fencing, EF bypass attempts, atomic outcome/event/ownership storage and failed commit/cancellation rollback. Existing migration inventory verification now compares all discovered migrations to applied migrations.

## Source evidence

See [the specification](../specs/002b1-submission-checkpoints.md) for pinned d498de6 sender, dispatcher, recovery and test references, and the approved differences in durable persistence timing. This is storage behavior; no additional IPS protocol behavior was inferred from the newly added business PDFs.

## Verification

- Restore and Release build: pass, zero warnings/errors.
- Full suite: 329 passed, zero failed/skipped (201 unit/architecture/Contracts, 128 integration).
- Formatting and whitespace checks: pass.
- Official migration generation: `dotnet ef migrations add Pacs008SubmissionEvidence --project src/IPS.Middleware.Infrastructure --context TransactionDbContext --output-dir Transactions/Migrations` (EF CLI 10.0.12).
- EF pending-model check: no changes. SQL tests migrate isolated LocalDB databases through the complete chain, including rollback to zero and reapplication. No application/production database was used.
- Pre-cleanup clean source export: tool/package restore, Release build, all 329 tests and EF model check pass without prior build outputs. Export: C:\Users\omo\AppData\Local\Temp\IPS-Middleware-checkpoints-rwou8zl_.

Owner cleanup was independently reviewed again; both axes found no actionable issues. After cleanup, Release build, formatting and EF model consistency passed, along with 201 unit/architecture/Contracts and 48 non-SQL integration tests. Full SQL verification was attempted but interrupted after repeated connection failures: the existing MSSQLLocalDB instance could not start (SQL error 50, LocalDB 0x89c5010a). Explicit instance startup also failed. The 329-test result above describes the pre-cleanup implementation. No tests were modified or disabled; rerun the full suite once LocalDB is available before merging.

## Standards

Independent read-only review found no actionable findings. The repository is a concrete persistence boundary; authorization reuse is justified, and payment policy remains separate from shared save orchestration.

## Spec

Independent read-only review found no actionable findings against the focused specification. Committed prerequisites, immutable evidence, original ownership, concurrency and rollback are covered. Review did not imply transport or end-to-end crash recovery is implemented.

## Remaining work and limits

Stage 2b.2 must implement processing/resume decisions and tests at every checkpoint. Stage 2c owns HTTP transport, supervision, deadlines and reliable callback work. No payment endpoint is introduced here. No remote call is authorized by a failed or repeated marker write. DevelopmentUnsigned is stored evidence, not environment permission; the workflow must enforce the signer policy at dispatch. Raw evidence means the decoded response body plus supplied headers, not original network bytes. Later resend attempts require their own explicit design; the initial marker is never overwritten.

The existing ownership expiry check occurs at staging time; parent rowversion fences a writer after ownership recovery. There is no new lease renewal or database-clock commit deadline in this slice.
