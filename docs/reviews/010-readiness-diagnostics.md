# Review 010: readiness and diagnostics

Branch codex/payment-initiation, stacked on 009 (2c44d57) and the unmerged slices 005a-005d, 007a-007c and 008a-008c; no merge is approved. On 2026-10-07 the owner chose readiness and diagnostics as the first operational slice and approved the [specification](../specs/010-readiness-diagnostics.md) with all three recommended decisions (plain-text status, instrument only, cached backlog gauges). Commit and merge approval are pending.

## Delivered

- **Readiness.** `GET /health/ready` (liveness unchanged) runs three `ready`-tagged checks and returns only `Healthy`, `Degraded` (200) or `Unhealthy` (503): the database (reachable, applied migrations equal the model's, only when an enabled feature uses it), the supervised workers (`SupervisedBackgroundService.Health` from loop heartbeats: disabled, not started, running, stalled, draining, stopped, faulted) and the loaded certificates of enabled transports (Degraded within `CertificateWarning`, Unhealthy once expired). A problem is logged when it appears or changes.
- **Metrics.** One meter `IPS.Middleware` (`PaymentMetrics`, no package, no exporter): outcome counters from committed domain events in the unit of work, resends, callbacks by result, receipts and acknowledgements, incoming registrations and core events, proxy calls, validation rejections, logged errors by component, one HTTP duration histogram by client and result, and gauges for due work by kind, the age of the oldest, the inbound journal by status and the snapshot age, read from a SQL snapshot every 15 seconds (`BacklogSnapshotService`).
- **Correlation.** `WorkScope` opens a log scope per unit of outgoing, incoming and receive work.
- **Configuration.** `Diagnostics` section (validated at startup), `docs/configuration.md`, `docs/architecture.md`. No migration, no behaviour change.

## Changed existing tests

- `IncomingWorkerSqlTests`: its service setup registers `IncomingTransportSettings`, which the receive worker now takes (for its heartbeat period).
- `IncomingTransferTests` (worker acknowledgement test) and `OutgoingStatusDeliveryTests` gain metric assertions and move into the non-parallel `Metrics` collection (the meter is process-wide). Nothing is weakened.

## New tests

Readiness checks against real SQL (healthy, pending migration, unknown migration, unreachable, unused), worker states, certificate windows, settings; the endpoint through the real host with stand-in checks (200, 503, degraded, only the status in the body); metrics through a `MeterListener` (committed and uncommitted saves, rejected and uncertain outcomes, incoming events, HTTP results, resends, proxy outcomes, validation rejections, error counts, callbacks, receipts and acknowledgements); backlog reader and service against real SQL; log scopes of concurrent outgoing and incoming work.

## Verification

Build 0 warnings; format and imports checks clean; no pending EF changes; full suite recorded in the presentation.

## Independent reviews

**Standards.** No layering violation. Fixed: the heartbeat of a sweep now also beats after the pass, the SQL-retry loop of the receive worker keeps beating, a readiness problem is no longer logged on every probe, the misspelled `ips.ips.resends` is `ips.resends`, validation of the settings reads as named predicates and rejects a NaN factor or a snapshot interval over a day, the ready tag is one constant, the static meter is justified in a comment, sleep-based test waits were replaced with task waits, and the unit test no longer assumes it is the only counter in its process. Recorded: `ICertificateInventory` has one implementation and exists for the certificate check's tests; the `Start`/`Elapsed` helpers are thin.

**Spec.** No blocking divergence. Fixed: acknowledgement failures count in `ips.errors`, the settings key (`BacklogSnapshot`) is in the spec, staleness of the gauges is visible (`ips.backlog.snapshot_age`), a failed-callback assertion. Recorded in the spec notes: the `abandoned` callback result, trust anchors counting toward expiry, and two gaps without tests (the real sweep loops' heartbeat periods; `/health/ready` through the real checks with live transports).

## Limits

The thresholds (stall factor 3, warning window 30 days, snapshot 15 seconds) are defaults to tune against a real deployment. Readiness does not call IPS, the CBS or the Proxy Solution, so a remote outage appears in the HTTP metrics and the circuit breakers, not in readiness. No exporter is attached.
