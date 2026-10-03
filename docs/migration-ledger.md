# Capability migration ledger

## Reference

Source: [vaso514d/IPS.MiidleWear](https://github.com/vaso514d/IPS.MiidleWear/tree/d498de6c4638aa71cdb20189d13642b41abab5f1), commit `d498de6c4638aa71cdb20189d13642b41abab5f1`.

Source tests: 233 passed, 0 failed, 0 skipped on 2026-10-03. These are evidence to inspect, not a substitute for protocol requirements. File paths below are relative to that pinned source tree.

Approved differences: one executable host; new internal layering/names; a fresh database schema when storage is implemented; exclusion of reporting and standalone helper hosts/tools. No external contract or payment-behavior differences are approved.

## Foundation

Status: Ready for review; merge approval pending. Release build, 28 tests, formatting, Kestrel startup, and package metadata checks pass. See [foundation review](foundation-review.md).

Delivered: independent repository, seven projects, dependency rules, imported Contracts and immutable baselines, minimal host, integration smoke tests, build/format/test configuration, architecture decisions, and contribution instructions.

The production layers remain empty until a capability requires implementation. No payment requests are accepted at this milestone.

## Ordered capability backlog

| ID | Capability | Source evidence | Acceptance scenarios | Status | Approved behavior differences |
|---|---|---|---|---|---|
| 1 | Transaction lifecycle and durable storage | `Domain/Entities/Transactions/PaymentTransaction.cs`; `Persistence/Transactions/PaymentTransactionStore.cs`; tests `Domain/PaymentTransactionTests.cs`, `Domain/WriteReliabilityTests.cs`, `Domain/InboundMessageIdempotencyTests.cs` | Durable intake before success; status/history consistency; repeated references across message types; concurrent insert/claim; restart with pending work; real SQL concurrency | Planned | None |
| 2 | Outgoing pacs.008 | `API/Controllers/GatewayController.cs`; `API/Transactions/OutgoingTransactionIntake.cs`, `Pacs008TransactionSender.cs`; tests `Api/Pacs008InstantPaymentTests.cs`, `OutgoingTransactionFlowTests.cs`, `IpsV1FieldProfileTests.cs` | Valid/invalid HTTP inputs; 202 Processing after storage; generated identifiers; correct XML/signature/headers; accept/reject outcomes; status query and existing delivery acknowledgement semantics | Planned | None |
| 3 | Outgoing reliability and status delivery | `API/Transactions/TransactionRecovery.cs`, `Pacs008StatusInvestigator.cs`; `Gateway/Services/TransactionStatusDelivery.cs`; tests `Api/TransactionRecoveryTests.cs`, `MockIpsEndToEndTests.cs`, `Gateway/TransactionStatusDeliveryTests.cs` | Connection failed before send versus lost reply after processing; pacs.028 investigation; duplicate-safe resends; deadlines; restart recovery; status callback retry and idempotency; manual review | Planned | None |
| 4 | Incoming payment handling | `Application/BackgroundServices/IpsInboundReceiverService.cs`; `Gateway/Services/CoreApiInboundMessageHandler.cs`, `InboundCoreReconciliation.cs`; tests `Gateway/InboundAckOrderingTests.cs`, `InboundReadProcessingSplitTests.cs`, `InboundCoreReconciliationTests.cs`, `IncomingStatusReportApplierTests.cs` | Store receipt before acknowledgement; duplicate delivery; core callback result; pacs.008 reply versus MessageAck ordering; lost core response; incoming status application; restart and lease loss | Planned | None |
| 5 | pacs.009 and pacs.004 | `API/Transactions/IsoTransactionSenders.cs`; `Gateway/Services/Pacs009InboundPaymentMapper.cs`, `Pacs004InboundPaymentMapper.cs`; tests `Api/Pacs009SchemaTests.cs`, `MockIpsEndToEndTests.cs` | Outgoing/incoming transfer and return; original transaction references; correct schema and acknowledgement path; type-specific outcomes/recovery | Planned | None |
| 6 | Recalls and payment initiation | `API/Services/RecallXmlMessageBuilder.cs`, `Pain002XmlMessageBuilder.cs`; `Gateway/Services/Pain001InboundPaymentMapper.cs`; tests `Api/RecallMessageTests.cs`, `PaymentInitiationMessageTests.cs`, `Gateway/Pain001InboundTests.cs` | camt.056 request; camt.029 refusal and pacs.004 acceptance; incoming pain.001; outgoing PSP-prefixed pacs.008 or pain.002 refusal; initiation deadline and core acknowledgement semantics | Planned | None |
| 7 | Proxy management | `API/Proxy/`; Contracts `Proxy/`; tests `Proxy/ProxyRequestValidationTests.cs`, `ProxyUpdateEndToEndTests.cs`, `AcmtSchemaTests.cs` | Register/update/remove; optional and partial fields; signatures and XML; accept/reject; HTTP transport errors; preserve existing routes/results | Planned | None |
| 8 | Operational completion | `Application/DependencyInjection/ObservabilityExtensions.cs`; `Application/HealthChecks/`; `Gateway/Services/SqlInboundChannelLease.cs`; tests `Domain/ObservabilityTests.cs`, `XmlStorageAndShutdownTests.cs`, `Options/ConnectionPoolSizingTests.cs` | Readiness reflects configured dependencies; safe draining; bounded queues; multi-instance coordination; correlation and meaningful metrics; measured load behavior | Planned | None |

For source paths, replace the initial `Domain/`, `Application/`, `Persistence/`, `API/`, or `Gateway/` with `IPS.MiidleWear.<name>/`. Test paths are under `IPS.MiidleWear.Tests/`.

Each row is a capability sequence, not a mandate for one large PR. Split coherent increments and record their commits, specification, checks, review findings, and approval here. Essential persistence, diagnostics, and failure handling ship with the capability that needs them.

## Gaps to resolve before implementation

- The current inbound handler forwards pacs.008/009/004 and pain.001 to core and applies pacs.002. Other received types are archived/acknowledged. Do not infer a new recall callback from DTO availability alone.
- MockProxy implements query/notification operations beyond the three public proxy management operations. Those extra mock features are not additional rebuild scope.
- DTO declarations preserve existing mixed JSON casing; validators, generated XML values, and HTTP error behavior must be characterized from runtime code/tests.
- Storage records and audit timing reflect previous optimizations. Establish required durability and acknowledgement guarantees before choosing the new schema.
- The pacs.008 foundation should establish reusable receive processing; add other message-specific paths with their capabilities rather than placeholder handlers.
