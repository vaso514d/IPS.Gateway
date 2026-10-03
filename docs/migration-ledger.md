# Capability migration ledger

## Resume checkpoint — 2026-10-03

Read this checkpoint after compaction, in a new session, or before continuing implementation. Follow the [capability-by-capability workflow](../CONTRIBUTING.md#capability-by-capability-rebuild-workflow).

- Purpose: preserve the original capability-led rebuild as an alternative to the commit-by-commit process. This branch is codex/capability-rebuild, created from codex/durable-intake at 4639ce6. Its descendant review branches follow the same process unless the owner changes it.
- Copy repository: D:\vaso\Running\IPS\IPS.Middleware. Read-only source: D:\vaso\Running\IPS\IPS.MiidleWear, pinned at d498de6c4638aa71cdb20189d13642b41abab5f1.
- Completed implementation anchors: foundation 705eeb3; lifecycle/state/history d2ed3ac; durable outgoing intake and atomic SQL updates 1e1e924. Reuse this code. No merges have occurred; exact owner merge approval is still unrecorded.
- Latest implementation verification: 78 passing tests, including real SQL tests; build, formatting, migration consistency, and independent reviews passed. This branch setup changes documentation only and is not a new test run.
- Next implementation slice, when the owner resumes this alternative: 1c, pending-work discovery, safe claims, and restart recovery foundations. First establish its behavior specification from the pinned dispatcher/recovery code and tests. No 1c production changes exist yet.
- Keep payment sending, investigation/resends, and protocol-specific behavior in their later capabilities. Introduce workers and typed clients with concrete capability needs. The empty-worker/client request from the commit-by-commit discussion was interrupted before any edits and is not the next action on this alternative branch.
- The source-history audit remains on codex/durable-intake. It identified 84 original commits with e448c97 as the first open substantive commit; that audit does not dictate execution order on this branch.
- Branch creation alone preserves this option. No capability implementation, merge, remote creation, publication, or cutover is requested by this setup.

## Reference

Source: [vaso514d/IPS.MiidleWear](https://github.com/vaso514d/IPS.MiidleWear/tree/d498de6c4638aa71cdb20189d13642b41abab5f1), commit `d498de6c4638aa71cdb20189d13642b41abab5f1`.

Source tests: 233 passed, 0 failed, 0 skipped on 2026-10-03. These are evidence to inspect, not a substitute for protocol requirements. File paths below are relative to that pinned source tree.

Approved differences: one executable host; new internal layering/names; a fresh database schema when storage is implemented; exclusion of reporting and standalone helper hosts/tools. No external contract or payment-behavior differences are approved.

## Foundation

Status: Ready for review; merge approval pending. Release build, 28 tests, formatting, Kestrel startup, and package metadata checks pass. See [foundation review](foundation-review.md).

Delivered: independent repository, seven projects, dependency rules, imported Contracts and immutable baselines, minimal host, integration smoke tests, build/format/test configuration, architecture decisions, and contribution instructions.

The foundation starts with empty internal libraries. Capability 1a adds transaction state/history to Domain on its review branch; capability 1b adds Application intake and Infrastructure SQL storage on its review branch. No payment requests are accepted.

## Capability scope backlog

| ID | Capability | Source evidence | Acceptance scenarios | Status | Approved behavior differences |
|---|---|---|---|---|---|
| 1 | Transaction lifecycle and durable storage | `Domain/Entities/Transactions/PaymentTransaction.cs`; `Persistence/Transactions/PaymentTransactionStore.cs`; tests `Domain/PaymentTransactionTests.cs`, `Domain/WriteReliabilityTests.cs`, `Domain/InboundMessageIdempotencyTests.cs` | Durable intake before success; status/history consistency; repeated references across message types; concurrent insert/claim; restart with pending work; real SQL concurrency | 1a and 1b implemented; merges pending; 1c is next when this workflow resumes | None |
| 2 | Outgoing pacs.008 | `API/Controllers/GatewayController.cs`; `API/Transactions/OutgoingTransactionIntake.cs`, `Pacs008TransactionSender.cs`; tests `Api/Pacs008InstantPaymentTests.cs`, `OutgoingTransactionFlowTests.cs`, `IpsV1FieldProfileTests.cs` | Valid/invalid HTTP inputs; 202 Processing after storage; generated identifiers; correct XML/signature/headers; accept/reject outcomes; status query and existing delivery acknowledgement semantics | Planned | None |
| 3 | Outgoing reliability and status delivery | `API/Transactions/TransactionRecovery.cs`, `Pacs008StatusInvestigator.cs`; `Gateway/Services/TransactionStatusDelivery.cs`; tests `Api/TransactionRecoveryTests.cs`, `MockIpsEndToEndTests.cs`, `Gateway/TransactionStatusDeliveryTests.cs` | Connection failed before send versus lost reply after processing; pacs.028 investigation; duplicate-safe resends; deadlines; restart recovery; status callback retry and idempotency; manual review | Planned | None |
| 4 | Incoming payment handling | `Application/BackgroundServices/IpsInboundReceiverService.cs`; `Gateway/Services/CoreApiInboundMessageHandler.cs`, `InboundCoreReconciliation.cs`; tests `Gateway/InboundAckOrderingTests.cs`, `InboundReadProcessingSplitTests.cs`, `InboundCoreReconciliationTests.cs`, `IncomingStatusReportApplierTests.cs` | Store receipt before acknowledgement; duplicate delivery; core callback result; pacs.008 reply versus MessageAck ordering; lost core response; incoming status application; restart and lease loss | Planned | None |
| 5 | pacs.009 and pacs.004 | `API/Transactions/IsoTransactionSenders.cs`; `Gateway/Services/Pacs009InboundPaymentMapper.cs`, `Pacs004InboundPaymentMapper.cs`; tests `Api/Pacs009SchemaTests.cs`, `MockIpsEndToEndTests.cs` | Outgoing/incoming transfer and return; original transaction references; correct schema and acknowledgement path; type-specific outcomes/recovery | Planned | None |
| 6 | Recalls and payment initiation | `API/Services/RecallXmlMessageBuilder.cs`, `Pain002XmlMessageBuilder.cs`; `Gateway/Services/Pain001InboundPaymentMapper.cs`; tests `Api/RecallMessageTests.cs`, `PaymentInitiationMessageTests.cs`, `Gateway/Pain001InboundTests.cs` | camt.056 request; camt.029 refusal and pacs.004 acceptance; incoming pain.001; outgoing PSP-prefixed pacs.008 or pain.002 refusal; initiation deadline and core acknowledgement semantics | Planned | None |
| 7 | Proxy management | `API/Proxy/`; Contracts `Proxy/`; tests `Proxy/ProxyRequestValidationTests.cs`, `ProxyUpdateEndToEndTests.cs`, `AcmtSchemaTests.cs` | Register/update/remove; optional and partial fields; signatures and XML; accept/reject; HTTP transport errors; preserve existing routes/results | Planned | None |
| 8 | Operational completion | `Application/DependencyInjection/ObservabilityExtensions.cs`; `Application/HealthChecks/`; `Gateway/Services/SqlInboundChannelLease.cs`; tests `Domain/ObservabilityTests.cs`, `XmlStorageAndShutdownTests.cs`, `Options/ConnectionPoolSizingTests.cs` | Readiness reflects configured dependencies; safe draining; bounded queues; multi-instance coordination; correlation and meaningful metrics; measured load behavior | Planned | None |

For source paths, replace the initial `Domain/`, `Application/`, `Persistence/`, `API/`, or `Gateway/` with `IPS.MiidleWear.<name>/`. Test paths are under `IPS.MiidleWear.Tests/`.

These rows determine capability execution order on this branch. Split coherent increments and record their commits, specification, checks, review findings, and approval here. Essential persistence, diagnostics, and failure handling ship with the capability that needs them.

## Review 1a: transaction state and history

Branch: codex/transaction-lifecycle. Temporary base: codex/foundation at 705eeb3, because foundation merge approval is still pending. No capability merge is authorized.

Specification and source evidence: [001a-transaction-lifecycle.md](specs/001a-transaction-lifecycle.md). Terminology: [CONTEXT.md](../CONTEXT.md).

Implemented: transaction identity/direction/references, current state, immutable status and processing observations, UTC normalization, explanation normalization, and final-state classification. There are no new packages, adapters, endpoints, or workers.

Verification: Release build passes with zero warnings/errors; 58 tests pass (30 lifecycle, 21 architecture/compatibility, 7 host). Architecture and Contracts checks remain enforced.

Capability 1b now implements atomic intake, duplicate handling, generated SQL schema, and atomic status/history updates with SQL tests. Capability 1c will cover pending-work queries, claims, and restart recovery. None of these guarantees were established by 1a alone.

Approved external behavior differences: none. Source gaps and internal improvements are recorded in the specification. Independent Standards and Spec reviews have no remaining findings. Formatting and diff whitespace checks pass. [Review evidence](reviews/001a-transaction-lifecycle.md) is ready; owner approval remains pending.

## Current review: 1b durable intake and updates

Branch: codex/durable-intake. Base: lifecycle d2ed3ac. The branch is stacked for review; no merge has been approved.

[Specification and source commit map](specs/001b-durable-intake.md). Relevant original changes: 0202969 for intake/concurrency and 8b9f38c for atomic request persistence, read at final reference d498de6.

Implemented: Application intake; transaction-specific storage interface; SQL Server persistence of requests/current state/history; client-reference uniqueness across message types; explicit update outcomes; coherent reads; atomic status/history writes; generated initial EF migration and repository-local EF tooling.

Verification: 78 tests pass (57 unit/architecture/compatibility; 21 integration, including 14 real SQL cases). Generated migration apply/rollback/reapply and model consistency pass. Test databases are isolated LocalDB databases created and removed by the fixtures. Fresh-checkout verification and formatting pass. Standards and Spec reviews have no remaining findings after the read-consistency and design-time configuration fixes. [Review evidence](reviews/001b-durable-intake.md) includes the rebuild commits and migration command. Owner merge approval is pending.

Remaining: 1c must establish pending-work selection, claims/leases, and restart recovery. Host registration and HTTP intake arrive with outgoing payment endpoints. Deployment collation and performance under long histories require later validation. No external behavior difference is approved.

## Gaps to resolve before implementation

- The current inbound handler forwards pacs.008/009/004 and pain.001 to core and applies pacs.002. Other received types are archived/acknowledged. Do not infer a new recall callback from DTO availability alone.
- MockProxy implements query/notification operations beyond the three public proxy management operations. Those extra mock features are not additional rebuild scope.
- DTO declarations preserve existing mixed JSON casing; validators, generated XML values, and HTTP error behavior must be characterized from runtime code/tests.
- Storage records and audit timing reflect previous optimizations. Establish required durability and acknowledgement guarantees before choosing the new schema.
- The pacs.008 foundation should establish reusable receive processing; add other message-specific paths with their capabilities rather than placeholder handlers.
