# Specification 010: readiness and diagnostics

Status: specification on codex/payment-initiation, stacked on 009 (2c44d57) and the earlier unmerged slices 005a-005d, 007a-007c and 008a-008c (merge approval pending for all of them). The owner chose, on 2026-10-07, readiness and diagnostics as the first operational slice, with graceful shutdown and multi-instance coordination (011) and measured performance (012) to follow one at a time. The owner approved the specification with the recommended decisions on 2026-10-07; implemented, review pending.

## Scope

Make the running host observable without changing what it does: a readiness endpoint that says whether this instance can take traffic, metrics that show what the payment engine is doing, and log scopes that tie every line of a workflow to its payment. Today the host has `GET /health/live`, logging in a few places and no metrics. Not in scope: any behaviour change of the payment, incoming, proxy or worker logic; graceful-shutdown and multi-instance proofs (011); load and latency measurement (012); an exporter, dashboards or alert rules; authentication of the health endpoints.

## Design

- **Readiness.** `GET /health/ready` (liveness stays as is) runs ASP.NET Core health checks registered only for what is enabled, so a host with every feature off is Ready. Checks:
  - `database`: when any database-backed feature is enabled, the SQL connection opens within a short timeout and the applied migrations equal the model's (a pending or unknown migration is Unhealthy, since no code migrates the database at startup).
  - `workers`: each enabled supervised service (outgoing runtime, the four incoming workers) reports it is running and that its last completed sweep or loop is within a multiple of its configured interval; a stalled or faulted worker is Unhealthy, one still draining is Unhealthy so a stopping instance leaves the pool.
  - `certificates`: every loaded signing, TLS client and signature-trust certificate (outgoing, incoming, proxy) is currently valid; one expiring within 30 days is Degraded (still Ready, reported), an expired one is Unhealthy.
  The response is plain text (`Healthy`, `Degraded` or `Unhealthy`) with HTTP 200 for Healthy and Degraded and 503 for Unhealthy; per-check detail is logged, not returned, so nothing about certificates or configuration is exposed on an unauthenticated route.
- **Metrics.** One `System.Diagnostics.Metrics` meter, `IPS.Middleware`, with no new package and no exporter (a host adds OpenTelemetry or another listener later):
  - counters: outgoing sends by message type and outcome (accepted, rejected, uncertain, manual review), possible-duplicate resends, CBS callbacks by result, incoming receipts by message type and result (processed, held, duplicate, acknowledged, acknowledgement failed), incoming core deliveries by result, proxy calls by operation and result (accepted, rejected, timed out, failed), validation rejections by operation;
  - histograms: IPS exchange duration and Proxy exchange duration, CBS call duration;
  - observable gauges read from a cached SQL snapshot refreshed every 15 seconds (not per scrape): outgoing work due, incoming work due, callbacks awaiting delivery, held receipts, and the age in seconds of the oldest due item per kind.
  Instrumentation sits at the existing named workflow steps (outcome recorded, reply decided, callback result), through one small `PaymentMetrics` type with named methods; no behaviour depends on it.
- **Correlation.** Each outgoing, incoming and proxy workflow runs inside a log scope with the payment id, message type, client reference or message id and attempt, so one grep or query follows a payment across the request, the worker and the recovery. Existing log messages keep their text; the failures that are swallowed for recovery today (worker loop, observed work, acknowledgement) also increment an error counter by component.
- **Configuration.** `Diagnostics` section: `BacklogSnapshot` on/off (default true; the key is not `Enabled`), `SnapshotInterval` (15 seconds), `CertificateWarning` (30 days), `WorkerStallFactor` (3), `DatabaseTimeout` (3 seconds), all validated at startup.

## Owner decisions (2026-10-07, all recommended)

1. **Readiness shape:** plain text status, 503 only when Unhealthy, detail only in logs.
2. **Exporter:** instrument only (a `Meter`, no package); the deployment attaches OpenTelemetry or a collector.
3. **Backlog gauges:** included, from one cached SQL snapshot every 15 seconds per instance.

## Acceptance

Real SQL and the existing simulators:

- Readiness: Ready with every feature disabled; Ready with a reachable migrated database and running workers; Unhealthy for an unreachable database, a pending migration, a stopped, faulted or stalled worker and an expired certificate; Degraded (HTTP 200) for a certificate within the warning window; Unhealthy while a worker drains; liveness is unaffected by all of these; the body never contains configuration or certificate detail.
- Metrics: each counter and histogram is recorded exactly once per outcome in the existing end-to-end tests (read through a `MeterListener`): accepted, rejected and uncertain sends, a flagged resend, a callback success and failure, an incoming receipt processed, held, duplicate and acknowledged, a failed acknowledgement, a proxy accept, reject, timeout and failure; the gauges reflect seeded due work and the oldest age and refresh on the interval; no metric changes any result.
- Correlation: a captured log of one outgoing and one incoming workflow shows the scope values on every line of that workflow, and concurrent workflows do not mix.
- No existing test changes except the mechanical host start-up expectations; build, full tests, format, EF model unchanged (no migration), independent Standards and Spec reviews.

## Limits

What an operator needs in production is unproven until the host runs against a real IPS; the thresholds (stall factor, warning window) are defaults to tune. Readiness does not call IPS, the CBS or the Proxy Solution, so a remote outage shows in metrics and the circuit breakers, not in readiness.

## Implementation notes

- Outgoing and incoming outcomes are counted from the committed domain events in the unit of work (`CommittedEventMetrics`), once per event and never for a save that did not commit, instead of at each workflow step: `ips.outgoing.status_changes` by message type and status, `ips.outgoing.outcomes_observed`, `ips.incoming.registered` by kind and `ips.incoming.core_events` by kind, operation and result. Processed and held receipts are the gauge `ips.inbound.journal` by status (from the snapshot) rather than counters.
- The three duration histograms are one, `ips.http.duration`, tagged by HTTP client (`outgoing-ips`, `outgoing-cbs-status`, the incoming clients, `proxy`) and result (`ok`, `http_error`, `timeout`, `failed`), recorded in the single place every HTTP exchange passes. The resend counter `ips.resends` is not split by message type, which the client does not know.
- Log scopes carry the workflow (`outgoing`, `incoming`, `receive`) and the work key (payment id, journal id or IPS sequence with the message type). The message type and client reference of an outgoing payment are not in the scope, because the work is started by id before the payment is loaded; the proxy exchange has no scope because it holds no state to correlate.
- Readiness checks live in the Api (they use the framework health-check types); the Infrastructure exposes `SupervisedBackgroundService.Health`, `ICertificateExpirySource` and `DatabaseUse`. A worker loop reports with `Beat`; sweeps beat each iteration and the receive loop beats each poll with the receive timeout plus its delays as its period.
- The `Diagnostics` section is in `appsettings.json` and `docs/configuration.md`. No migration and no behaviour change.
- Added after review: a gauge `ips.backlog.snapshot_age` shows how stale the backlog gauges are; the resend counter is `ips.resends`; sweeps beat before and after each pass and a single pass longer than the stall factor times its interval reads as stalled; acknowledgement failures also count in `ips.errors`; a readiness problem is logged when it appears or changes, not on every probe. `ips.cbs.callbacks` also has an `abandoned` result for a callback whose owner expired (no CBS call is made then). Expired trust anchors count toward certificate expiry, because a chain that no longer validates stops the transport.
- Not covered by a test, and recorded as a limit: that each real sweep loop and the receive loop beat with their intended period (the loops share one `RunSweepsAsync` and one `Beat` call), and `/health/ready` through the real checks with live transports (the checks are tested directly and the endpoint with stand-in checks).
- Found by the Aspire environment (012) with containers under load: a stall bound of three times a 200 millisecond sweep interval (0.6 seconds) turned a briefly busy worker Unhealthy, so a loop is now stalled only after `WorkerStallFactor` times its period plus `WorkerPassAllowance` (default 10 seconds, setting `Diagnostics:WorkerPassAllowance`) for one pass.
