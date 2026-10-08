# Review 2c.2a — Outgoing transport and bounded evidence persistence

Branch codex/outgoing-execution, base 0837192. Owner approved commit and merge on 2026-10-05. This is the transport prerequisite of 2c.2, split from endpoint/supervisor integration for focused review. No endpoint, worker, schema migration or Contracts change.

## Changes and source evidence

OutgoingHttpClients implement initial IPS submission and frozen CBS status callbacks. Settings and certificate owners are independent from incoming activation. Shared Infrastructure/Transport contains the previously implemented HTTP timeout/breaker, full-body buffering, file/store certificates, TLS validation and immutable evidence mechanics; incoming adapters use the same extracted mechanics without changing their policy or reservations.

Pinned source d498de6: Application/Transport/IpsHttpStpClient.cs, IpsHttpHeaders.cs and Tests/Api/IpsHttpStpClientHeaderTests.cs establish POST Message, exact UTF-8 XML, participant/version/keep-alive headers, gzip and absence of the resend header on an initial send. Source EnsureSuccessStatusCode and header-only timeout are intentionally not copied: the approved journal/transport specification requires full unsuccessful evidence and a complete-exchange timeout. Callback path, DTO and key come from unchanged Contracts and the preceding reviewed status-delivery slice.

Pacs008Processing replaces unlimited post-send save cancellation suppression with a configurable 2-second evidence budget. It starts after remote completion, covers response then interpretation saves, and is disposed with the run. A call canceled before evidence remains recoverable from its marker; a save timeout leaves the previous committed checkpoint intact.

## Verification

- New focused suite: 29 passed, including real SQL persistence expiry/recovery and independent HTTP fixtures.
- Full suite: **784 passed, zero failures/skips** (249 unit/architecture/Contracts + 535 integration) using LocalDB and JDK20 for independent signatures. Incoming HTTP/TLS, worker and host tests remain green after the shared extraction.
- Release build: zero warnings/errors. EF reports no pending model changes; no migration generated.
- Two additional regression tests passed for incoming/outgoing certificate independence and evidence-budget timing after a slow call. Total distinct tests verified: **786** (784 full-suite + 2 added regressions); the production logic is identical across both runs.
- Formatting verification and whitespace checks pass after removing two trailing blank lines. Local generated results: TestResults/outgoing-transport.trx in each test project.

No live external systems were contacted. Tests use ephemeral certificates, local HTTP/TLS simulators and isolated LocalDB databases. Transport settings alone never activate an outgoing payment endpoint or worker. Connection ceilings are per instance and additive across separately enabled directions.

## Standards

Independent review: no hard documented violations or actionable smells. Shared mechanics reflect demonstrated reuse; direction-specific certificate owners preserve separate configuration and lifetimes.

## Spec

Independent review: zero findings against 002c.2a. Wire contracts, independent activation, single-attempt transport and bounded evidence persistence match the focused specification.

## Next

After owner approval, 2c.2b adds send/status endpoints, service-owned bounded attempts, callback dispatch, recovery and shutdown. Full process termination/multi-host endpoint verification belongs to that increment. The approved 200/504 metadata exception is deferred until those endpoints exist.
