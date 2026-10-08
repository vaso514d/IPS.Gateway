# Stage 2c.2 — Outgoing HTTP and supervised execution

Branch: codex/outgoing-execution. Verified base: 0837192, owner-approved callback/status-read increment merged into codex/capability-rebuild on 2026-10-05. This document refines the approved [Stage 2c specification](002c-outgoing-http.md); implementation is split into [2c.2a transport and bounded evidence persistence](002c2a-outgoing-transport.md), followed by 2c.2b endpoints and supervised execution. Preserve the original repository and existing branches. No production activation.

## Source and existing components

Pinned original d498de6: API/Controllers/GatewayController.cs; API/Transactions/TransactionStatusReader.cs; Application/Transport/IpsHttpStpClient.cs and IpsHttpHeaders.cs; Tests/Api/IpsHttpStpClientHeaderTests.cs. Preserve POST Message with UTF-8 application/xml, X-MONTRAN-IPS-Channel, X-MONTRAN-IPS-Version and Connection: keep-alive. The initial send carries no possible-duplicate resend header. Keep gzip decompression. CBS callbacks use the existing status DTO and Idempotency-Key from the frozen snapshot.

Source transport calls EnsureSuccessStatusCode and ends its timeout after response headers. Do not copy those limitations: the approved journal requires unsuccessful/malformed response evidence, and the approved timeout covers the complete exchange. Existing incoming HttpEvidence, response buffering, certificates and single-attempt resilience demonstrate reusable mechanics. Extract only demonstrated shared transport mechanics; outgoing activation must not depend on enabling incoming workers.

Reuse Pacs008Intake, Pacs008Processing, OutgoingTransactionWork, OutgoingStatusDelivery and OutgoingStatusReader. Api maps Contracts; Application retains validation and processing decisions; Infrastructure owns execution scopes, scheduling and transport. Do not add a second payment algorithm or change the generic unit of work.

## HTTP contract

- POST /api/ips/pacs008/send maps the unchanged request DTO to Application input. New invalid input returns existing-compatible 400 problem details. Preserve field names and reference normalization.
- Newly committed intake requests immediate supervised processing. Normal submission does not enter a channel. The response waits at most 30 seconds from durable intake, including admission time.
- Return 200 with TransactionStatusDto for a committed final outcome, including business rejection. At the wait deadline, unresolved state returns 504 with that same DTO. A Processing result from one attempt does not end the HTTP wait early as success.
- Duplicate reference returns 200 with current committed status immediately, before changed-policy validation. It neither initiates another attempt nor replaces the accepted request nor acknowledges callbacks.
- GET /api/ips/transactions/status validates messageKind and clientReference, preserves 404 for missing/kind mismatch, and uses the exact-outcome acknowledgement workflow. POST responses do not acknowledge callback work.
- Update only pacs.008 success route metadata from 202 to 200 and describe 504. Record the approved compatibility exception with independently specified baseline expectations; preserve all other Contracts bytes and public surfaces.

## Execution and cancellation

Use a service-owned supervisor with bounded admitted tasks and fresh scopes. Take an execution slot before acquiring SQL ownership. Caller disconnect or HTTP deadline stops only waiting after intake; service lifetime and attempt budget control processing. Do not retain an HTTP request scope, spawn unobserved tasks or allow unbounded tasks waiting for admission. If immediate admission is unavailable, leave the durable payment recoverable and let the bounded recovery path supply a later attempt while HTTP continues observing SQL.

Defaults: outbound concurrency 8, IPS timeout 25 seconds, HTTP wait 30 seconds, attempt budget 35 seconds, ownership 45 seconds. Keep the separate 20-second first-submission window measured from AcceptanceDateTime. Persist raw response before interpretation. Replace Pacs008Processing.Run's unbounded CancellationToken.None commit suppression with a short configurable evidence-persistence allowance (initial 2 seconds), started when evidence needs saving after remote completion/failure, not before a 25-second call. Validate attempt plus persistence below ownership, transport timeout below attempt budget, and shutdown budgets. Caller cancellation must never be substituted for service cancellation.

Stop admission and discovery first at shutdown; drain tracked attempts within the configured shutdown budget, then cancel service execution and allow bounded evidence persistence. Observe every exception and leave incomplete SQL work recoverable. Dispose failed scopes; never retry a failed unit of work.

## Transport and settings

Add outgoing-specific opt-in settings under Payments:Outgoing for participant, IPS/CBS URLs and paths, pool limits, certificates, timeouts, breakers, concurrency and recovery. Reuse certificate loading and trust rules: XML signing, TLS credentials and IPS signing trust remain distinct; validate at startup, restart for rotation, secrets untracked. Explicit development-only unsigned behavior remains unchanged.

Use separate named outgoing IPS and callback pools. Validate outbound admission against its own IPS capacity and bound callbacks to their CBS pool. Document combined incoming plus outgoing connection limits per service instance; incoming receive/follow-up reservations and dispatch sizing must remain unchanged. Do not enable database migration at startup.

Each adapter invocation sends at most once. Microsoft resilience supplies timeout/circuit breaker only, without retry or hedging; redirects remain disabled. Preserve complete HTTP status, decoded body and all response/content/trailing header values for the existing IPS interpreter and journal. Test failures during response-body reading, not just headers.

## Recovery and callback dispatch

Only recovery uses a bounded payment-ID channel. SQL discovery refills at startup and periodically; full/lost/duplicate notifications confer no ownership and lose no durable work. Request-triggered processing and recovery compete through existing SQL claims. Use a fresh recovery scope before a processing scope, so abandoned markers without responses become Uncertain and cannot authorize another original send. Saved responses replay without sending; unfinished preparation resumes only while the first-send deadline allows it.

Schedule callback work independently from original-payment execution and HTTP response completion. Discover current due StatusDeliveryKey records through the existing repository; acquire callback ownership immediately before dispatch. Preserve frozen payloads, durable retry rounds and exact-outcome acknowledgement. No new callback storage is needed.

Uncertain/manual-review/final payments never restart original submission automatically. pacs.028 and protocol-authorized resending remain the following reliability review.

## Verification and owner gate

Use real SQL and independent HTTP/protocol simulators. Cover exact route/headers/encoding/JSON; final acceptance and rejection 200; unresolved 504; immediate duplicate Processing; validation and concurrent intake; synchronous success retaining callbacks; disconnect and HTTP timeout while processing continues; bounded admission; disabled-host behavior; incoming/outgoing activation independence; complete-exchange timeout and no hidden retries; status-query acknowledgement races.

Run two hosts against one database and verify one initial submission under request/recovery competition. Terminate at intake, preparation, submission marker, response and final-outcome boundaries. A remotely processed payment with a lost reply must remain uncertain without blind resend. Test callback crash/recovery, shutdown, capacity and preserved incoming operation.

Run full SQL, architecture/Contracts, host, build, formatting and EF model checks. No schema migration is expected; if one is justified, generate it with the official EF CLI. Obtain independent Standards and Spec reviews and update the ledger. Present the implementation for owner approval before commit/merge. Transport 2c.2a is owner-approved and merged as 03f4976. The remaining [2c.2b host integration](002c2b-outgoing-host.md) is implemented on codex/outgoing-host for independent verification and owner review.
