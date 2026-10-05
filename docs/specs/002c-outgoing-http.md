# Stage 2c: outgoing HTTP, supervised execution and status delivery

Branch: codex/outgoing-http. Base: c7c38b9, merged into codex/capability-rebuild with owner approval on 2026-10-05. Incoming Review 004c.3 is complete. The owner authorized continuing to outgoing HTTP. This specification makes the already-approved Stage 2c concrete; implementation and its independent reviews are still pending. No endpoint is activated by this document.

## Evidence and existing building blocks

Pinned original: d498de6c4638aa71cdb20189d13642b41abab5f1.

- IPS.MiidleWear.API/Controllers/GatewayController.cs: SendPacs008Async stores intake; GetTransactionStatusAsync delegates status access.
- IPS.MiidleWear.Contracts/Abstractions/IGatewayApi.cs and Pacs008/Pacs008RestApiRoutes.cs: POST /api/ips/pacs008/send; GET /api/ips/transactions/status. The imported POST metadata currently says 202.
- IPS.MiidleWear.API/Transactions/OutgoingTransactionIntake.cs: trimmed, globally unique clientReference; duplicates return the original transaction without replacing its request.
- IPS.MiidleWear.API/Transactions/TransactionStatusReader.cs: query requires message kind and reference; mismatched kind or missing reference returns 404; reading pending final-status delivery acknowledges it.
- IPS.MiidleWear.Application/Transactions/TransactionStatusMapping.cs: Received/Sending/Uncertain/Investigating/Resending map to Processing; Processing hides the internal description. Preserve every other DTO field and enum value.
- IPS.MiidleWear.Gateway/Services/TransactionStatusDelivery.cs, CoreReferences.cs and Contracts/Abstractions/IClientPaymentReceiver.cs: callback POST /api/ips/transactions/status/receive; any 2xx acknowledges delivery; Idempotency-Key is clientReference:status; delivery is at least once, and changed outcomes cannot be acknowledged by an old callback completion.
- IPS.MiidleWear.Application/Options/IpsTransactionOptions.cs: callback defaults are three attempts per round, five seconds between attempts, ten minutes between rounds, unlimited rounds (0), discovery every five seconds.
- IPS.MiidleWear.API/Infrastructure/ContractEndpointFilter.cs: validation uses HTTP 400 problem details. Keep JSON and validation field names compatible rather than importing this filter wholesale.

The rebuilt Pacs008Intake already validates new requests, returns existing references before revalidation, and stores normalized immutable snapshots. Pacs008Processing already prepares, signs, marks submission before I/O, saves responses and interprets correlated signed replies. OutgoingTransactionWork recovers expired claims. Reuse these workflows; do not implement another send algorithm in the endpoint.

Before implementing each review, inspect the corresponding original tests (Api/OutgoingTransactionFlowTests.cs, Api/Pacs008InstantPaymentTests.cs, Api/IpsHttpStpClientHeaderTests.cs and Gateway/TransactionStatusDeliveryTests.cs) and transport code. Record any additional contradictions in that review's evidence.

## Review order

Owner-approved adaptation on 2026-10-05 inserts [2c.0 explicit outgoing journal](002c0-outgoing-journal.md) before these two reviews. The journal prerequisite is merged as f9a8030. [2c.1 callback/status-read implementation](002c1-outgoing-status-delivery.md) is approved and merged as 0837192. The [2c.2 execution specification](002c2-outgoing-execution.md) is split into [2c.2a transport and bounded evidence persistence](002c2a-outgoing-transport.md), under review on codex/outgoing-execution, then 2c.2b endpoint/supervisor integration. Endpoint implementation has not started.

Keep two focused review gates within this stage:

1. **2c.1 Durable status delivery and reads.** Application status projection, atomic callback obligation creation, frozen callback payload, fenced claims/completion, retries and status-query acknowledgement. Test with SQL and a fake remote boundary. No public endpoints or hosted activation yet. This follows approval and merge of the journal prerequisite.
2. **2c.2 HTTP and supervised host integration.** DTO mapping, actual IPS and CBS callback adapters, send/status endpoints, service-owned attempts, restart discovery, configuration and shutdown. Add route metadata compatibility exceptions only in this review. Use a new branch after owner approval of 2c.1.

No endpoint ships without its callback durability dependency. Each review includes specification refinements, tests, independent Standards/Spec reviews and owner approval before merge.

## HTTP behavior

- POST /api/ips/pacs008/send accepts the unchanged Pacs008InstantPaymentRequestDto; Api maps it to Application input. Application owns business validation. Invalid new requests return 400 without durable intake.
- Only newly committed intake starts an immediate attempt. The caller waits up to 30 seconds measured after durable intake; waiting for an execution slot consumes this wait budget.
- A committed final result, including business rejection, returns HTTP 200 and TransactionStatusDto. If still unresolved at the deadline, read committed current state and return HTTP 504 with the same DTO, transaction identity and current status. A workflow returning Processing early does not mean final success; observe committed state until the wait ends without repeatedly submitting.
- A repeated clientReference immediately returns HTTP 200 and the original current status, including Processing. It never starts another attempt, changes identifiers, replaces the request, or acknowledges callback delivery. SQL decides concurrent intake winners.
- GET /api/ips/transactions/status preserves query shape, validation, kind matching and 404. Acknowledgement must target the particular outcome returned, using concurrency protection; a racing newer outcome must remain deliverable.
- Neither successful synchronous POST nor duplicate POST acknowledges callbacks. The explicit status query retains its existing acknowledgement semantics.
- Change only pacs.008 success metadata from 202 to 200, documenting unresolved 504. Preserve public types, route constants, JSON shapes and unrelated endpoint metadata. Update independently specified compatibility expectations; retain original provenance.

## 2c.1 status delivery

Represent delivery as Infrastructure persistence metadata, separate from payment business outcome and incoming reversal delivery. Application owns callback scheduling/retry decisions through feature-owned repository and remote interfaces. Use the existing scoped context and shared unit of work.

When an outgoing outcome becomes reportable (Accepted, Rejected, NotSent, ManualReview or ManuallyResolved), freeze its status identity and complete callback data and stage delivery in the same transaction as the aggregate outcome, events and ownership release. Repeated/conflicting observations retaining the same outcome must not create new delivery. A later explicit manual resolution creates its own obligation. A failed save creates neither outcome nor callback work.

Claims are committed before remote I/O. Freeze callback payload and idempotency key across retries. Any 2xx succeeds; timeout, lost reply or non-2xx remains delivery failure/uncertainty and follows the durable retry schedule. A callback may be repeated because CBS must process the unchanged key idempotently. Do not apply the one-submission reversal rule to outgoing status callbacks.

Preserve the source retry defaults above through validated settings; no automatic HTTP retry or hedging. Retry counters, due time and acknowledgement survive restart. Stale owners or replies for an older outcome cannot acknowledge newer work. Status reads and callback completion races must converge without erasing an unobserved outcome. Inspect source terminal/exhaustion behavior before finalizing the persistence model; do not silently change it.

Use official EF CLI migrations if a new delivery table or metadata is required, preserve historical migrations and fresh-database-only support. Keep Domain free of transport/storage dependencies, and do not turn the shared unit of work into an outgoing-only service.

## 2c.2 execution, transport and recovery

The host owns a bounded supervisor and independent scope per admitted attempt. Caller cancellation only stops HTTP waiting after intake; it never cancels or disposes the service-owned attempt. All tasks are tracked, exceptions observed, and shutdown drains/cancels them under explicit budgets. Durable SQL discovery recovers intake committed before notification, preparation checkpoints and abandoned claims. Request execution and discovery compete via the same SQL claim.

Defaults remain: IPS call timeout 25 seconds, HTTP wait 30 seconds, attempt budget 35 seconds, ownership 45 seconds, outbound concurrency 8. Keep the existing 20-second submission-start window measured from client AcceptanceDateTime separate from those budgets. Waiting 30 seconds does not extend eligibility to submit. Replace unbounded post-submission cancellation suppression with a configurable bounded evidence-persistence allowance that fits inside ownership and shutdown budgets.

Transport performs one send per invocation with timeout covering response body reading. Reuse demonstrated certificate loading and HTTP mechanics from incoming transport without coupling outbound activation to incoming workers. No HTTP retries, hedging or redirects. Preserve original SendMessage route, headers, encoding and raw response evidence after inspecting the pinned transport. Signing, TLS credentials and IPS signature trust stay distinct. Settings are validated at startup, secrets remain untracked, and live activation stays explicit.

Validate outgoing admission against actual configured connection capacity. Document whether incoming/outgoing use separate pools and their combined per-instance limits; never silently consume the incoming receive reservation or change the approved incoming dispatch formula.

A marker without a response remains Uncertain and is never automatically resent. pacs.028 investigation and safe resend policy remain the next reliability capability. Preserve uncertain work for that capability rather than fabricating rejection or reporting full uncertainty recovery as implemented.

## Verification

2c.1: atomic state/event/callback rollback; complete payload round trip; repeated observations; competing claims and stale completion; exact retry boundaries; restart after callback marker/lost reply; any-2xx delivery; final-status read acknowledgement; read versus new outcome and callback completion races; no cross-effects on incoming reversal records.

2c.2: exact request/status JSON and routes; new request validation; final acceptance/rejection 200; unresolved 504; immediate duplicate 200 before changed-policy validation; concurrent duplicates; no callback suppression after synchronous success; caller disconnect and wait expiry while processing continues; execution admission bounds; request/recovery competition across hosts; shutdown; process termination around every durable checkpoint; saved response replay; no hidden transport retries; timeout includes slow bodies; uncertain remote outcome never resent.

Run full SQL tests, architecture/Contracts checks, Release build, formatting, generated model consistency and host liveness. Use independent HTTP/protocol fixtures; no real service or production activation. Update review evidence and checkpoint at each gate.
