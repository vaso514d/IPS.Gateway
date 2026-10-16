# 004b.2b review: incoming CBS processing and recovery

Branch: codex/incoming-processing. Base: 8cd2248. Implemented on 2026-10-04 through the owner-invoked implementation skill, including commit authorization. Merge into codex/capability-rebuild requires owner approval.

## Behavior and diff guide

The incoming workflow now commits its submission marker before calling CBS, saves complete response evidence before interpreting it, and resumes without a second submission after an interrupted attempt. CBS outcome, immutable IPS decision and durable follow-up obligations remain separate. Missing explicit status never becomes acceptance. Unknown outcomes produce RJCT/MS03 and reconciliation; late credits retain reversal work.

- Domain/Inbound: guarded processing operations and immutable, explicitly named versioned events. Conflicting final reports preserve the original outcome and require manual review.
- Application/Inbound/Pacs008: frozen originating context, processing budgets and concrete orchestration through repository/client/interpreter seams. Receipt completion is separate.
- Infrastructure/Repositories/Inbound and Persistence: tracked checkpoints under committed ownership, parent rowversion fencing, immutable call evidence, and one submission per payment. The existing general unit of work is unchanged.
- Infrastructure/Inbound/Pacs008: explicit final-status and optional-identifier correlation interpretation of CBS JSON.
- Tests/Inbound: domain invariants, malformed/correlated replies, real SQL crash recovery, ownership races, cancellation, deadline boundaries and a stateful credit simulator.

Source evidence: original commit d498de6c4638aa71cdb20189d13642b41abab5f1, IPS.MiidleWear.Gateway/Services/InboundCoreCaller.cs, CoreSystemRequest.cs, CoreSystemClient.cs, InboundCoreReconciliation.cs and CoreReferences.cs, plus imported IClientPaymentReceiver/Pacs008PaymentResultDto contracts. Approved differences and acceptance rules are in [the specification](../specs/004b2b-cbs-processing.md).

## Verification

- Release build: zero warnings and errors.
- Full suite: 509 passed, zero failed/skipped (223 unit/architecture/Contracts and 286 integration), including isolated real SQL Server LocalDB databases and Java signature verification.
- New coverage: 4 domain, 18 interpreter and 29 SQL workflow cases. Crash injection exercises failures before and after committed markers, responses and final decisions. Recovery verifies one submission, one status query and one credit after a lost reply; marker-only recovery queries without submitting.
- Formatting verification and EF pending-model check pass. Existing SQL migration-chain tests and database fixtures exercise the complete generated chain. Existing host smoke tests pass.
- Official migration command: `dotnet ef migrations add IncomingCbsProcessing --project src/IPS.Middleware.Infrastructure --context TransactionDbContext --output-dir Transactions/Migrations --configuration Release`.
- Generated migration Up/Down and snapshot inspected: incoming state/context columns plus IncomingCoreCalls, foreign key, uniqueness and completion constraints. Historical migrations preserved. Fresh databases only; no existing-data conversion.

## Independent reviews

Standards review: no actionable findings. Application owns processing decisions, Infrastructure owns persistence and protocol interpretation, Domain remains free of infrastructure dependencies, and shared save orchestration stays general.

Spec review: no remaining actionable findings after corrections. A conflict observed before the first IPS decision could previously clear ManualReviewRequired; the decision now retains it and a dedicated ordering regression passes. The simulator now retains credits independently and tests recovery after a lost reply. Additional storage tests verify committed ownership, committed call/response prerequisites, immutable evidence, headers and submission uniqueness.

## Limits and next review

No live CBS/IPS transport, worker, endpoint, acknowledgement, reply XML storage or delivery is enabled. FollowUpAtUtc persists the first ten-second follow-up deadline but this slice does not discover or execute reconciliation or reversal work. Lease checks remain staging-time checks with rowversion fencing, without heartbeat renewal or a database-clock commit deadline. Simulator evidence does not establish live CBS interoperability.

After owner merge approval, specify 004b.2c reconciliation and reversal execution, including retry cadence, acceptance-versus-completion handling and manual-review policy. Preserve this branch and the other development branches.

## Owner-requested cleanup — 2026-10-05

The owner found the slice too verbose and the Application folders overloaded, and asked for a cleanup informed by current practice and libraries. Specification: [004b2b-cleanup.md](../specs/004b2b-cleanup.md). Behavior is preserved except the owner-approved difference D1. The work is uncommitted on codex/incoming-processing above 0bc010c.

- **Feature folders.** Application/Inbound now has Receipts, Registration, Processing and Pacs008. Each feature owns its ports, and Application/Abstractions/Inbound is gone. Grab-bag files were split into CoreCall, IncomingProcessingSnapshot and IncomingRegistration. In Infrastructure, IncomingPaymentColumns (formerly IncomingProcessingColumns) and IncomingPaymentJson moved to Persistence/Inbound. Moves were made with `git mv`; the uncommitted renames show in `git diff HEAD -M`.
- **Processing.** ProcessingBudget holds the deadline arithmetic. The workflow checks the budget once, after the ownership recheck, and dispatches through one helper. IncomingPayment.HasFinalCoreOutcome replaces the repeated outcome checks.
- **Aggregate.** DecideIps is straight-line code, and one RequiredFollowUp rule serves both decision and late-result paths. Repeat observations use one helper.
- **Interpreter.** Strict System.Text.Json deserialization replaces the manual JSON walk: case-insensitive names, duplicate names rejected, and case-insensitive extension data. D1, approved by the owner on 2026-10-05: a duplicate name nested inside an unmapped field now yields Unknown instead of acceptance.
- **Persistence helpers.** Typed HasLiveClaim and SetClaim replace hand-copied claim checks and clearing in both incoming payment repositories.
- **Options.** AddInboundFoundations registers IncomingProcessingOptions with TryAdd, and IncomingPaymentIntake requires it, replacing the silent default.
- **Libraries.** System.Text.Json strict mode is used now. Polly or Microsoft.Extensions.Http.Resilience is deferred to the live CBS client and must never retry the POST. The options validation generator is deferred to configuration binding, outside Application. Stateless, sagas and Temporal were declined. The workflow already matches the atomic-phases and idempotent-consumer guidance.

New tests (25):
- an IPS decision truth table (9 cases);
- ProcessingBudget boundaries (7);
- interpreter cases: case-variant and exact duplicate unmapped names, case-variant duplicate mapped names, a null root, a wrong field type and D1 (7);
- the UTC processing-time instant (1);
- options registration that keeps a host's own instance (1).

Existing tests changed only in `using` lines, except the IncomingPaymentTests intake helper, which now passes default options. Mutation checks confirmed that the truth table, the duplicate-name cases and the exact-expiry ownership test fail when the rewritten rules are broken.

Verification:
- restore and Release build: zero warnings or errors;
- 534 tests pass: 239 unit/architecture/Contracts and 295 integration on LocalDB;
- formatting verification passes;
- EF reports "No changes have been made to the model since the last migration";
- `git diff --check` is clean;
- no Contracts or package changes.

Size: production code under src changes by +202/−166 lines. Moves and splits add file headers, and ProcessingBudget plus the typed claim helpers are new named units, so the slice is about the same length. The gain is structure and readability rather than line count: the workflow, repositories and interpreter shrank, while the aggregate grew by ten lines for named rules.

Independent cleanup reviews:

- **Standards.** No Critical findings. Fixed:
  - Renames had dropped out of the index, so the diff was incomplete; intent-to-add was restored.
  - The 004b.2 spec still prescribed Abstractions/Inbound; it is now marked superseded.
  - Code had grown; the interpreter DTO became a positional record, and ProcessingBudget became a compact record struct without restating doc comments. A redundant aggregate comment was removed.
  - The architecture paragraph and the helper name in the spec were inaccurate and were corrected.

  Recorded for the owner:
  - Registration and Processing reference each other's types. Payment ownership sits in Registration and the frozen context in Processing, as the approved layout prescribes. Moving ownership to Processing and the context to Registration would reduce, not remove, the cycle, because intake also needs the payment window.
  - ClaimToken.IsModified is still read directly next to the typed helper.
  - Scheduling options are a parameter while processing options use TryAdd.
- **Spec.** No Critical or Important findings. It verified equivalence of:
  - the decision, follow-up and event paths;
  - the budget arithmetic;
  - the removed early budget check, with an identical committed result and no remote call;
  - DI resolution.

  A differential harness over 2,229 reply inputs found only the two D1 differences. Documentation wording was corrected. It also confirmed a pre-existing gap: a lone UTF-16 surrogate in a response body throws ArgumentException in both versions. HTTP decoding prevents this today, so it is deferred to the live CBS client review.

## Follow-up review — 2026-10-05

Reviewed the owner's pending cleanup against 0bc010c. Independent Standards and Spec reviews found no confirmed actionable production-code issue. Clarified the strict-JSON rule: top-level names compare case-insensitively, while nested unmapped JSON rejects exact duplicate names and permits case variants. Added a passing case for nested `a`/`A`; no runtime behavior was changed.

Current verification: Release build passed with zero warnings/errors; 239 unit/architecture/Contracts tests and all 27 reply-interpreter cases passed. Formatting, whitespace and EF pending-model checks passed. The full-suite attempt was stopped after repeated LocalDB startup failures (error 50 / 0x89c5010a); a direct startup retry also failed. SQL verification is blocked for this review, so the earlier 534-test result above is historical evidence, not a fresh result. The initial focused build overlapped the failing SQL run and hit locked test output; rerunning after stopping that run succeeded.

Cleanup and review edits remain uncommitted, with merge approval pending. Next action: restore LocalDB availability and rerun the full suite before treating current SQL verification as complete.

## Merge approval — 2026-10-05

Owner approved committing and merging the reviewed cleanup with the LocalDB limitation recorded. Cleanup commit 2c7b04e and implementation 0bc010c are now on codex/capability-rebuild after a successful fast-forward from codex/incoming-processing. The working branch is checked out and the review branch remains preserved. This supersedes the pending/uncommitted status above; no production changes were made during merge.
