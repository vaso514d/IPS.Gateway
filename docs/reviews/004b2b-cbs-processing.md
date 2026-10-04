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
