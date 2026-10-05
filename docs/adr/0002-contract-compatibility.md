# ADR 0002: Preserve the external contract while rebuilding internals

Status: Accepted for the foundation.

## Context

The owner wants a clean rebuild that existing core banking consumers can adopt without changing their HTTP contract or .NET contract references.

## Decision

Import Contracts unchanged from source commit `d498de6c4638aa71cdb20189d13642b41abab5f1`. Preserve package/assembly identity, version, namespaces, public members, route metadata, JSON shapes, status meanings, and callback contracts. Keep the original net8.0 target and dependency-free package.

Use `IPS.Middleware` for new internal projects. Translate Contracts at Api and Infrastructure edges; Application references Domain only. Use frozen public-interface and serialization baselines against the referenced Contracts assembly to detect accidental changes. The source manifest records initial import provenance; compatible source edits do not require byte-for-byte identity.

## Consequences

The existing spelling and mixed JSON property casing remain part of compatibility. Preserving declarations does not copy the old controller convention, error filter, or workflow implementation. Each capability must prove HTTP behavior separately when its endpoint is added.

A deliberate external change requires an approved compatibility/versioning decision and new independent expectations. Never refresh a baseline from the changed implementation merely to make a failing test pass. Internal database design is free to change; existing data migration is outside this milestone.

## Approved synchronous outbound exception (2026-10-04)

The owner approved replacing initial outgoing 202 with final 200 (including business rejection) or unresolved 504 with TransactionStatusDto after 30 seconds of durable intake. Duplicate submissions immediately return 200 current status, including Processing, and never start another attempt. Methods, routes, JSON shapes, reference semantics, callbacks and status-query acknowledgement remain preserved. Implement route metadata and independent baseline changes explicitly in Stage 2c; the aggregate refactor changes no Contracts source or baseline. See [the approved plan](../rebuild-plan.md).

## Approved incoming reconciliation behavior (2026-10-05)

A CBS reconciliation 404 remains unknown rather than proving that no credit occurred. Retry until the fixed window expires, then require manual review. A CBS rejection-notification success means reversal request acceptance, not reversal completion. Send at most one automatic reversal request; accepted, unsuccessful or uncertain delivery requires manual review until authoritative completion/idempotency guarantees are established. Preserve the existing TransactionStatusDto callback shape and route, with incoming direction, original EndToEndId as CoreReference, canonical payment ID and frozen IPS rejection details. No Contracts or baseline changes. See [004b.2c.1](../specs/004b2c-incoming-reconciliation.md).

## Incoming reply delivery decisions (2026-10-05)

The owner approved two total reply sends with a 200 ms delay and durable manual review on exhaustion, plus keeping completed duplicate receipts completed without another CBS call or reply send. The owner provisionally agreed to send the same frozen reply after the original processing deadline; IPS retention/late-acceptance behavior remains an explicit future verification question before production activation. Preserve the original decision and exact saved XML throughout recovery.

The new interpreter follows Annex D rather than the source receiver's unchecked successful-return shortcut: require HTTP 200, documented request status and a trusted, correlated pacs.002 final outcome. A final report opposite to the stored decision requires review; missing/invalid evidence remains unresolved. No Contracts types, routes or baseline changes are needed, and live interoperability is not yet claimed. See [the specification](../specs/004b2c2-incoming-replies.md).

## Incoming HTTP adapters (2026-10-05)

Review 004c.1 keeps imported Contracts and baselines unchanged. CBS receive/status/reversal routes, JSON mapping, exact EndToEndId submission key and `in:{CoreReference}:{Status}` notification key match the pinned source. IPS uses GET/POST Message with the participant/version headers and UTF-8 XML replies. No MessageAck is exposed by this adapter. Unlike source EnsureSuccessStatusCode shortcuts, non-success status, headers and body remain evidence for the already-approved interpreters (including reconciliation 404 unknown). No payment endpoints or workers are enabled by this increment. Automatic redirects, retries and hedging are disabled; durable workflows own subsequent attempts.

## Incoming composition (2026-10-05)

Review 004c.2 changes no Contracts, endpoints, wire formats, payment identity, CBS outcomes or durable reply policy. It composes the reviewed workflows, preserving FF01 without CBS, no pacs.008 MessageAck, immutable per-receipt correlation and completed duplicates without another send. One-second configurable continuation scheduling and first-reply readiness are internal orchestration behavior. Live activation and IPS late-reply verification remain deferred.

Review 004c.3 activates incoming processing only through explicit worker configuration. HTTP methods, imported DTOs/routes, message correlation, two durable reply attempts and completed-duplicate behavior remain unchanged. Successful nonempty polling bodies are retained even when transport metadata is unsupported; they are held by existing processing rules. Incoming pacs.008 sends its stored pacs.002 and never MessageAck. Provisional late-reply retention/acceptance remains an IPS verification gate before production activation.

Outgoing journal adaptation (2026-10-05): the owner retained existing Contracts/routes/status DTOs, OutgoingPayment enum values and callbacks. The reference document's /api/operations and replacement CBS/payment status models are not adopted. Only outgoing internal persistence changes in Stage 2c.0; the 200/504 metadata exception remains for the later endpoint review. Response evidence commits before interpretation; pre-send recovery and service-owned execution remain required.

Outgoing status delivery (2026-10-05, Stage 2c.1): Contracts and routes remain byte-for-byte unchanged. Internal versioned snapshots map to the existing TransactionStatusDto and clientReference:externalStatus idempotency key. Any callback 2xx acknowledges its exact outcome; status queries preserve exact-outcome acknowledgement, including ManualReview. Superseded snapshots remain historical, with only the current sequence dispatchable. Attempts are durably reserved before sending, including abandoned calls, so crashes cannot reset retry accounting. Endpoints and the approved 200/504 metadata adjustment remain Stage 2c.2.

Outgoing host integration (2026-10-05, Stage 2c.2b) applies the previously approved pacs.008-only metadata exception: IGatewayApi.SendPacs008Async declares success200 and documents final200, unresolved504 and immediate duplicate200. The corresponding public-api.json method entry was explicitly edited; original baseline provenance and all wire fixtures remain. Other route metadata/types are untouched. Api response serialization omits null values as in the source wire configuration. Status query acknowledgement and reliable callbacks remain independent from POST results.
