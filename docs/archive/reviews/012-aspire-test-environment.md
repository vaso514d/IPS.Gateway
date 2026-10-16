# Review 012: Aspire test environment

Branch codex/payment-initiation, stacked on 011 (651c1ab) and the unmerged slices 005a-005d, 007a-007c, 008a-008c, 009 and 010; no merge is approved. On 2026-10-07 the owner asked for an Aspire starter that tests can use and that runs in containers, chose the scope (AppHost, SQL container, API and tests), the simulators as a never-shipped test-support project, and moved measured performance to 013, then approved the [specification](../specs/012-aspire-test-environment.md). The owner approved the commit on 2026-10-07; merge approval is pending.

## Delivered

- **`tests/IPS.Middleware.AppHost`** (Aspire 13.6.1): a SQL Server container with the shipped EF migrations applied once the database is ready; the simulators; one or more API instances (`--Middleware:Instances=n`), each a separate resource that sends its own protocol version, as projects or, with `--Middleware:Container=true`, as containers built from the published output and `src/IPS.Middleware.Api/Dockerfile`. All settings are injected as environment; certificates are generated for the run (public parts only mounted into containers) and deleted when the AppHost stops.
- **`tests/IPS.Middleware.Simulators`**: IPS (`POST /Message`, ACCP or RJCT, a pacs.002 signed with the service's own signer, the header answer for a pain.002), the CBS status callback, the Proxy Solution (`/PRX/register|update|remove`), a TLS endpoint for the service and a plain endpoint for the health check and the `/_sim` control API (received messages with flag and version, behaviours, release, reset).
- **`tests/IPS.Middleware.AspireTests`** (`Aspire.Hosting.Testing`; skipped with a reason when Docker is not running; run serially): ready and live; a pain.002 and a pacs.008 send with exactly one callback; a rejecting IPS (Rejected, and the callback carries Rejected); a lost reply recovered by one flagged resend of the same bytes; Proxy register, update, remove and a reject; invalid input is 400 and reaches no simulator; two instances share 40 payments (each sent once, none flagged, both instances took part, every callback once); the service run from its container image.
- **`src/IPS.Middleware.Api/Dockerfile`** (runtime-only, non-root, port from the environment, no configuration baked in), `docs/local-environment.md`, `docs/architecture.md`, the ledger. The only other change to `src/` is the readiness stall rule below.

## A defect the stack found (in 010)

With containers under load the readiness probe flapped: a worker with a 200 millisecond sweep interval was stalled after three intervals (0.6 seconds), so a briefly busy instance turned Unhealthy. A loop is now stalled after `WorkerStallFactor` times its period plus `Diagnostics:WorkerPassAllowance` (default 10 seconds); documented in 010's spec and `docs/configuration.md`, with a test. The Aspire suite then passed three times in a row.

## Changed existing tests

`ReadinessCheckTests` pass the allowance explicitly (it no longer has a default) and gain a test for it. Nothing else changed.

## Verification

Build 0 warnings; format and imports clean; no pending EF changes; the nine Aspire tests passed three consecutive runs and once more after the final changes (about 3 minutes each, with Docker Desktop started for the work). In the full solution run the unit (476) and integration (989) suites passed; the Aspire tests failed there with Aspire's one-minute container-creation timeout because `dotnet test` on the solution ran them at the same time as the integration suite, so CONTRIBUTING now runs the test projects one after another (`-m:1`). The sequential full run: Aspire 9/9, unit 476/476, integration 983/989; the six failures were crash and processing tests reaching their 10-second timeouts under load, and their six classes passed 117/117 when rerun alone (the run before, with Docker idle, passed 989/989).

## Business-flow audit (2026-10-07)

The [audit](business-flow-audit-2026-10-07.md) finding S2 is in this slice: a failed start (startup, readiness wait or timeout) left the started application undisposed because `Stack.StartAsync` threw before returning it. Fixed: the application is disposed and the original failure rethrown. The audit's production findings F1, S1 and F2 are the next slice, [012a](../specs/012a-audit-corrections.md).

## Independent reviews

**Standards.** No `src/` reference to the new projects or packages. Fixed: throwaway directories are deleted when the AppHost stops, only the public certificates are mounted into a container, the AppHost configuration parses with the invariant culture, a bound and cleanup on a hung publish, the live probe is attached, the "once" assertions observe a settle instead of stopping at the first match (and the reject test checks the callback status), the stall allowance has no silent default, the unused partial class and a blanket warning suppression are explained or removed. Recorded: the simulators duplicate part of the integration tests' in-process simulators (accepted, in the spec); the Docker probe runs at test discovery; the tag of the base image is not pinned.

**Spec.** The implementation matches the scope, design and acceptance. Fixed: the callback and message counts are now settled, the reject callback is checked, invalid input also leaves the callbacks empty, the `/health/live` probe, the spec notes on the `src/` change, the hidden migration order and certificate handling. Recorded: the simulators sign a pacs.002 body for a pain.002 that the service ignores.

## Limits

Behaviour is proved against simulators, not a real IPS, CBS or Proxy Solution; the incoming receive path is not simulated yet (first extension); the Linux-host case of a temporary directory the container user cannot read is untested (this work ran on Windows with Docker Desktop); container startup takes tens of seconds and needs the SQL Server and ASP.NET images.
