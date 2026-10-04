# 004c — Live incoming processing: clients, dispatch and recovery

Owner requested implementation on 2026-10-05. Deliver as three independently reviewed increments, with owner approval before each merge. Prerequisite: restore LocalDB, rerun the complete suite for af9b090, obtain owner approval to merge that reply slice, then branch from the verified capability-rebuild head. Preserve existing branches and the original repository.

## Established decisions

One executable and one configured participant BIC per deployment, with multiple active service instances sharing SQL. Each instance has one receive worker, one processing dispatcher, one response/retry worker and one separate CBS follow-up scheduler. The dispatcher starts supervised handler tasks as connection capacity allows; it does not await each payment sequentially. The first committed SQL claim establishes ownership. Reading or enqueueing a journal ID does not.

SQL owns all remote attempt budgets. Microsoft HTTP resilience supplies timeouts and circuit breakers, with no HTTP retry or hedging. The approved two-attempt reply limit is durable across restarts; CBS submission and reversal markers never authorize an automatic repeat.

## Review 1: HTTP clients and configuration

Implement Infrastructure IPS receive/reply, CBS submission/status and reversal-notification adapters. Add an Application receive interface with immutable raw message/transport metadata; defer schema/signature validation to processing. Preserve Contracts methods, routes, JSON and idempotency headers. Reuse the current CBS mapping and reversal mapping. Return unsuccessful HTTP statuses, headers and body to existing interpreters rather than losing evidence in EnsureSuccessStatusCode. Preserve transport failures separately.

Apply deadlines to full response reads, not just headers. Disable redirects for payment calls and all automatic retries/hedging. Keep base URLs, endpoint paths, connection limits, timeouts and circuit-breaker settings configurable and validated.

Support PFX/PEM files and Windows-store thumbprints. Load certificates/settings at startup; restart for rotation in this milestone. Separate XML signing, TLS client authentication, server certificate trust and IPS signature trust. Preserve hostname/chain checks and explicit development-only unsigned signing. No tracked secrets or certificate material.

Live processing remains disabled by default. Enabling it requires complete participant, SQL, transport and certificate configuration. No automatic schema migration, production activation or worker registration in this first review. Test adapters through independent HTTP/TLS simulators with disposable certificates.

Pinned source evidence at d498de6: Application/Transport/IpsHttpStpClient.cs and Application/Options/IpsStpOptions.cs; Gateway/Services/CoreSystemClient.cs, CoreSystemRequest.cs, CoreReferences.cs and Gateway/Options/CoreSystemOptions.cs; Application/Security/CertificateSource.cs and IpsTls.cs. Prefix original paths with IPS.MiidleWear. Supplied Annex D pp. 92-94 and 98-100 and Participant API Integration pp. 3-4 document receive/reply and concurrent connection behavior. Contracts and previous specifications take precedence over incidental source shortcuts, including reconciliation 404 staying unknown.

## Review 2: workflow composition

Add a concrete Application coordinator with Infrastructure scope orchestration where necessary:

1. Acquire receipt ownership, validate and register/locate the canonical payment.
2. Commit registration and release receipt ownership before CBS; CBS processing uses the independent payment claim.
3. Once the immutable IPS decision exists, run reply preparation and the first send immediately under a fresh receipt claim.
4. Commit any unresolved reply schedule before notifying the reply channel.

Use fresh scopes at boundaries and after failed units of work. Never span HTTP with SQL transactions. A receipt whose canonical payment is owned elsewhere waits through durable scheduling without resubmission. Keep trusted FF01 without a fictitious payment; hold untrusted, unsupported, invalid-sequence and conflicting receipts. Preserve per-receipt references and completed duplicates without new sends.

No MessageAck for incoming pacs.008: its protocol reply is the persisted pacs.002 after processing. Do not enable other message handlers or acknowledgement paths. Provisional late replay sends the exact saved reply within its remaining budget; IPS retention and late acceptance remain a verification gate before production activation.

## Review 3: workers and recovery

- Receive: one outstanding GetMessage per instance; commit receipt before nonblocking notification; polling stays independent of handlers.
- Processing: one dispatcher, capacity admission before claim, tracked concurrent tasks and a separate scope per handler.
- Response/retry: dispatch due replies through the same workflow, including saved replies never sent before a crash.
- CBS follow-up: bounded direct SQL discovery of payment IDs, independent of receipt/reply completion.

Maintain two bounded Guid channels containing journal IDs only. Separate inbound and reply discovery, oldest due first, excluding terminal and live-owned work. Refill on startup and periodically. Queue contents confer no ownership. Concurrent instances use shared SQL claims/rowversions without a singleton polling leader or fixed instance-count configuration. Generate any required routing/index migrations through official EF CLI.

Obtain execution capacity before claiming. Reserve CBS slots for follow-up and share IPS send capacity between first replies and retries. No detached tasks or unlimited task creation. On shutdown stop polling/admission, drain tracked attempts within the shutdown budget, then cancel; retain durable evidence and permit later recovery through claim expiry.

## Configurable defaults

| Setting | Default |
|---|---:|
| Hosted workers per role | 1 |
| Outstanding receive calls per instance | 1 |
| IPS connection budget per instance | 100, reserve 1 for receive |
| CBS connection budget per instance | 100, reserve 2 for follow-up |
| Processing capacity | Lesser of remaining CBS and IPS send capacity |
| Channel capacity / discovery batch | 256 / 100 |
| Discovery interval | 1 second |
| Receive timeout / connect timeout | 10 seconds / 2 seconds |
| Message / empty response delay | 0 / 250 ms |
| Receive error delay | 1 second |
| Graceful shutdown budget | 30 seconds |

Validate positive limits, pool reservations, timeout ordering and discovery bounds. Budgets are per instance and must fit deployment/operator limits; these defaults make no throughput claim. Existing durable processing/reply/reconciliation settings remain authoritative.

## Verification and review gates

Adapter tests verify wire shape, paths, idempotency, encoding, unsuccessful bodies, cancellation/slow bodies, certificate trust and zero hidden sends. Integration tests run two hosts against one SQL database and verify one CBS submission, fenced ownership, fixed reply limits, per-receipt correlation and independent follow-up. Kill processes at receipt, registration, remote marker, response and reply checkpoints. Cover saturation, lost wakeups, dependency outages, handler exceptions, expired claims, empty polling, capacity and shutdown.

Run build, full SQL suite, formatting, architecture/Contracts, migration consistency and host smoke checks. Obtain independent Standards/Spec reviews per increment; present evidence and wait for owner approval before merging. Update compatibility documentation and the resume checkpoint at every gate. Completion is the opt-in host running the complete incoming pacs.008 flow against simulators with restart/multi-instance recovery. No production activation or outgoing HTTP endpoint changes.

## Review 1 implementation notes

Prerequisite completed: LocalDB recovered, af9b090 passed all 637 tests and was fast-forwarded to codex/capability-rebuild with explicit owner approval on 2026-10-05. Review branch codex/incoming-clients starts at af9b090.

Receive returns the complete immutable HTTP evidence plus nullable header-derived message type/sequence and duplicate hint. It deliberately does not infer a message type from XML, classify a non-success as EMPTY, or register a receipt. Review 2/3 will route absent/unsupported transport types to holds while retaining raw XML; no schema/signature/business validation occurs in this adapter. Raw headers retain malformed sequence evidence in the response model. No journal schema changes are included here.

CBS POST uses unchanged EndToEndId as Idempotency-Key; status GET uses messageKind=Pacs008 and URL-escaped reference; reversal POST maps the frozen notification and uses `in:{CoreReference}:{Status}`. All paths preserve base URL prefixes. Microsoft.Extensions.Http.Resilience 10.10.0 provides a custom breaker/timeout pipeline with no retry or hedging strategies. The inner body-buffering handler brings the complete response under the budget. [Microsoft HTTP resilience guidance](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience) describes custom pipelines and the unsafe default retries that this implementation excludes.

Tests use independent Kestrel HTTP/TLS peers with disposable certificates. Windows Schannel required temporary named keys rather than ephemeral keys for TLS. No real IPS/CBS credentials or endpoints are used. Certificate-store missing-thumbprint behavior is checked read-only; successful store selection/ACLs remain an operator environment check, without adding certificates to machine stores in automated tests.
