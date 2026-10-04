# 004b.2c.1 — Incoming CBS reconciliation and reversal review

Implemented on codex/incoming-reconciliation from verified codex/capability-rebuild at 12644e8. Owner approved the implementation plan on 2026-10-05. Changes are uncommitted; merge approval is pending. [Specification](../specs/004b2c-incoming-reconciliation.md).

## Behavior and diff guide

- Application/Inbound/Reconciliation owns due discovery, acquisition, saved-result replay, fixed-window retries and one automatic reversal request. It queries the original participant/EndToEndId, never resubmits a payment, and commits late-credit reversal work separately before dispatching it in a later attempt.
- Domain keeps the first final CBS outcome and immutable IPS decision. Separate reversal delivery state records Started, Accepted, Unsuccessful or Uncertain. Every observed or abandoned reversal requires manual review; acceptance does not prove completion. IncomingReconciliationRecorded carries full follow-up/reversal details under the stable incoming-payment.reconciliation-recorded name, schema version 1.
- Infrastructure reuses the existing scoped context, general unit of work, parent rowversion and immutable call history. Follow-up uses the same payment claim with separate due metadata. Reversal request JSON is frozen with the marker, checked against canonical references and decision, and protected from replacement. SQL enforces one reversal per payment independently of the one initial submission constraint.
- The only shared execution helper, CoreCallExecution, handles timeout, service cancellation and raw completion capture for the two implemented workflows. It makes no decisions and commits nothing.
- Infrastructure maps the frozen notification to the existing incoming TransactionStatusDto. Contracts and baselines are unchanged. No live client, worker, endpoint, IPS reply artifact or host wiring is added.

Approved source differences: reconciliation 404 remains unknown; reversal delivery is acceptance-only and never automatically repeated; manual review requires authoritative external resolution. Retry cadence remains 10 seconds initially, then 30 seconds, 1 minute, 5 minutes and 15 minutes repeatedly. The 24-hour window starts at the immutable IPS rejection decision. A saved conclusive response is interpreted before expiry, but an expired window never authorizes new remote I/O.

Source evidence: original d498de6c4638aa71cdb20189d13642b41abab5f1, IPS.MiidleWear.Gateway/Services/InboundCoreReconciliation.cs, TransactionStatusDelivery.cs and CoreSystemClient.cs, IPS.MiidleWear.Gateway/Options/CoreSystemOptions.cs, and IPS.MiidleWear.Application/Transactions/TransactionStatusMapping.cs. Public wire shape remains defined by the imported IClientPaymentReceiver and TransactionStatusDto.

## Verification — 2026-10-05

LocalDB started successfully for this implementation, resolving the previous session's verification blocker without changing or deleting the instance.

- Full Release suite: **581 passed, 0 failed, 0 skipped** (249 unit/architecture/Contracts; 332 integration). Real isolated SQL databases, the complete migration chain, host smoke checks and independent Java signature fixtures are included.
- New coverage: 10 domain/options cases and 36 SQL reconciliation cases. Includes before/after-commit crashes, saved evidence after expiry, retry timing, remaining-window timeout, service cancellation, competing claims, exact ownership expiry, stale responses, event payload round trips, frozen payload tampering, SQL marker uniqueness and bounded discovery ordering.
- The independent simulator stores a remote reversal effect separately from request count. Lost-response and rollback/restart cases assert one request and one effect; marked recovery never sends again.
- Build: zero warnings/errors. Formatting verification, diff whitespace and EF pending-model checks pass.
- Generated migration: 20261004210431_IncomingReconciliation (timestamp supplied by EF tooling). Command: `dotnet ef migrations add IncomingReconciliation --project src/IPS.Middleware.Infrastructure --context TransactionDbContext --output-dir Transactions/Migrations --configuration Release`, local EF CLI 10.0.12.
- Reviewed Up/Down: adds reversal/manual-review state, call request JSON, follow-up index, expanded kind constraint, reversal uniqueness and request-kind JSON constraint. Historical migrations are preserved. This chain is for fresh databases only; rollback drops the added data and is not a production conversion strategy.

## Independent reviews

**Standards: 0 remaining actionable findings.** The sole nonblocking duplication observation was addressed by the concrete shared call-execution helper. Workflow decisions and persistence boundaries remain separate; no generic orchestration layer or additional interface was introduced.

**Spec: 0 actionable production findings.** The requested stronger simulator evidence was added and passed. Final recheck confirmed response replay before expiry, explicit unknown/rejection/credit handling, accepted-only reversal semantics, immutable evidence, committed ownership, preserved notification mapping and SQL uniqueness.

## Limits and next action

No live CBS interoperability or reversal-completion guarantee has been established. Accepted/uncertain/unsuccessful reversal delivery intentionally requires manual review; operator tooling is not part of this slice. Lease validation remains a staging-time check with rowversion fencing, without heartbeat renewal or a database-clock commit deadline. Existing outcome/conflict semantics remain unchanged.

Present this diff for owner approval before merging. Next focused review: durable incoming IPS reply context, exact signed pacs.002 artifacts, delivery work and trusted FF01 receipt-scoped replies. Live receive/processing/response-retry pools remain later work.

## Configuration revision — 2026-10-05

Owner requested configurable implemented HTTP-call budgets, retries and operational settings. Added Payments sections to Api appsettings.json with constructor-validated, immutable typed options resolved before serving requests. Covered outgoing preparation; inbound processing/first follow-up; reconciliation call/persistence budgets, window, retry sequence and repeat interval; scheduling; registration retry limit; existing signing policy. Host settings survive inbound-foundation registration. No live HttpClient exists yet, so no unused transport URL or connection-pool settings were introduced. [Configuration guide](../configuration.md) documents defaults, environment overrides, array merging and restart behavior.

The Spec review identified that changing Window could move existing cutoffs. Fixed by persisting write-once ReconciliationDeadlineUtc atomically with the initial IPS decision and follow-up schedule. Initial scheduling is clamped to that cutoff; recovery uses the stored value. Generated official migration 20261004211746_FrozenReconciliationDeadline adds the column and a SQL schedule/deadline constraint. Command: `dotnet ef migrations add FrozenReconciliationDeadline --project src/IPS.Middleware.Infrastructure --context TransactionDbContext --output-dir Transactions/Migrations --configuration Release`. Reviewed Up/Down and snapshot; historical migrations preserved, fresh-database chain only.

Current full verification supersedes the earlier counts: **603 passed, zero failed/skipped** (249 unit/architecture/Contracts, 354 integration including real SQL). The additional 22 tests cover configuration overrides and startup rejection, empty JSON retry arrays, immutable retry schedules, actual fresh-scope retry limits, initial configured deadline capture/clamping, and restart with a longer or shorter configured window. Build has zero warnings/errors; formatting, whitespace and EF model checks pass. Final independent Standards and Spec reviews have no remaining actionable findings.

Changes remain on codex/incoming-reconciliation, uncommitted and unmerged. Next remains owner review, then durable IPS reply artifacts/delivery.

Owner update (2026-10-05): explicitly authorized committing the reviewed reconciliation and runtime configuration changes. The commit containing this update is the reviewed implementation checkpoint. Merge approval remains separate; next work is preparing the durable reply specification.
