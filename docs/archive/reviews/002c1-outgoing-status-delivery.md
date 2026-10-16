# Review 2c.1 — Durable outgoing callbacks and status reads

Branch codex/outgoing-status-delivery, base f9a8030. Owner approved commit and merge on 2026-10-05. The journal prerequisite was approved, committed and merged as f9a8030.

## Delivered

Every reportable pacs.008 outcome freezes a versioned internal status payload with the aggregate state and events. Multiple transitions in one save preserve distinct snapshots; only the current outcome can dispatch. Callback attempts are reserved before remote I/O and survive crashes. Separate delivery claims, parent and delivery rowversions fence competing owners, query acknowledgement and later manual resolution. Frozen payloads and source-compatible idempotency keys remain unchanged across retries.

Application owns retry rounds, expiry handling, remote budgets and exact-outcome reads. Infrastructure supplies SQL storage, preparation-only interception and mapping to unchanged Contracts. The shared unit of work is unchanged. Validated appsettings expose the existing source retry defaults and execution budgets. No outgoing HTTP client, endpoint, worker or production activation is added.

Source evidence at d498de6: Gateway/Services/TransactionStatusDelivery.cs and CoreReferences.cs; API/Transactions/TransactionStatusReader.cs; Application/Options/IpsTransactionOptions.cs; Tests/Gateway/TransactionStatusDeliveryTests.cs. The specification records preserved any-2xx acknowledgement, idempotency, retry defaults and status-read semantics. Durable attempt accounting and history improve restart safety without changing external DTOs.

## Migration

Generated with the official EF CLI: `dotnet ef migrations add OutgoingStatusDelivery --project src/IPS.Middleware.Infrastructure --context TransactionDbContext --output-dir Transactions/Migrations --configuration Release`.

Artifacts: 20261005125838_OutgoingStatusDelivery.cs, its designer and the model snapshot. Adds only OutgoingStatusDeliveries, its composite identity, parent foreign key, rowversion, pending-work index and consistency constraints. Historical migrations are preserved. Full chain is supported on fresh databases only; tests use isolated disposable LocalDB databases, not application storage.

## Verification

- Focused callback SQL suite: **16 passed**, including both review regressions.
- Full suite: **755 passed**, zero failures/skips (249 unit/architecture/Contracts + 506 integration), using real LocalDB and JDK20 for independent signature fixtures. Existing host smoke and incoming integration tests also pass.
- Release build: zero warnings/errors.
- Formatting verification, git whitespace check and EF pending-model check: pass.
- Result files: each test project's TestResults/status-delivery-reviewed.trx (local generated evidence).

Tests cover any-2xx, configured and unlimited retry rounds, exact due boundaries, expired markers, cancellation before/after claim, competing owners, frozen payload mutation rejection, status acknowledgement during callbacks, old versus new outcomes, atomic rollback and repeated observations. The inter-query test uses a command interceptor to commit a manual resolution precisely between delivery selection and parent loading.

## Standards

Independent review: no hard rule violations or actionable code smells. Application/persistence responsibilities and the general unit of work remain intact.

## Spec

Independent review initially identified two P2 issues: missing intermediate snapshots when multiple reportable transitions share a save, and superseded claims during the delivery/parent loading race. Both are fixed with real SQL regression tests. Independent recheck: no remaining findings.

## Next gate and limits

Owner approval to commit and merge was received on 2026-10-05. Next is 2c.2 HTTP and supervised execution, including real callback transport and scheduling. This review tests the remote boundary with controlled responses; it does not establish live CBS callback interoperability. Unlimited rounds are the preserved default, not a delivery-latency guarantee. Existing ownership checks remain staging-time checks without lease heartbeats. No external contract baseline was changed.
