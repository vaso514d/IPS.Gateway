# 004c.3 — Incoming workers and recovery

Owner approved merging Review 2 and starting Review 3 on 2026-10-05. Branch codex/incoming-workers starts at cede1c5; codex/capability-rebuild now contains that reviewed commit. Follow the approved [live processing plan](004c-live-incoming.md).

## Confirmed direction — 2026-10-05

The owner withdrew the Livestock-Product, MassTransit and Azure Service Bus directions because they belonged to another chat. Retain the approved SQL-and-channels architecture: two bounded Channel<Guid> queues, SQL ownership/recovery, and four hosted worker roles. Do not add a message broker or transactional bus outbox. No production code or dependencies were changed during that planning detour.

## Implementation boundaries

Four hosted roles per instance: receive, processing dispatch, reply dispatch and CBS follow-up. Receive has one outstanding call and commits before notifying. Processing handlers run concurrently under bounded admission; fresh scopes and SQL claims remain authoritative. No MessageAck, unsupported payment handler, database migration at startup, outgoing endpoint or production activation.

Processing discovery excludes stored replies; reply discovery selects them, including unsent envelopes. A decision without an envelope remains processing work so first-reply readiness is committed through composition. Discovery and notification do not confer ownership. CBS follow-up discovers payment IDs separately. No new schema is expected.

Connection admission reserves one IPS connection for receive and two CBS connections for follow-up. Processing concurrency is min(IPS send capacity, CBS capacity minus follow-up reservations). First replies and retries share a semaphore acquired before the reply claim. Follow-up execution is bounded by its CBS reservation. Each dispatcher tracks every task and disposes phase scopes. Shutdown stops receive/admission, drains existing handlers up to the configured 30-second budget, then cancels and awaits their bounded evidence persistence.

Workers require a separate explicit Payments:Incoming:Workers:Enabled switch, validated transport, participant, SQL and reply protocol configuration. Transport can remain configured for standalone adapter use without starting polling. Defaults and budgets follow 004c. No database is created or migrated by the executable.

Receive accepts successful HTTP responses only. An empty successful body is an empty poll; a nonempty body is preserved even if the EMPTY header contradicts it. Missing/unusable message-type metadata is represented as unsupported and held by processing; XML is never inspected to infer a transport type. Missing/nonpositive sequence remains held under the approved receipt policy. Transport errors use the configured delay. A received message remains in the receive loop until its receipt commits, rather than issuing another receive after a transient database failure.

## Evidence and acceptance

Source d498de6 IPS.MiidleWear.Application/Transport/IpsHttpStpClient.cs GetMessageAsync establishes long polling, raw response collection and EMPTY handling. Its XML-based message-type inference is intentionally not copied (004c Review 1 notes). The supplied inbound-payment-flow.md establishes durable journal plus two ID-only channels; the owner's later approved plan fixes one instance per hosted role and adds independent CBS follow-up.

Verify routing/order/claim exclusions with real SQL; queue saturation and lost notifications; supervised concurrency, faults and drain/cancellation; empty/error polling and durable receipt retry; two service hosts sharing one database; one CBS submission per payment, immutable replies and durable attempt limits; independent reconciliation; process termination at receipt, registration, submission marker, saved response and reply checkpoints. Run full tests, architecture/Contracts, formatting, model consistency and host checks, then independent Standards and Spec reviews. Record any verification limitations. Commit and merge only after owner review.
