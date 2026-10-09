# Local environment (Aspire)

A whole deployment of the middleware on one machine, for local runs and for the Aspire tests: SQL Server in a container, simulators standing in for IPS, the CBS and the Proxy Solution, and one or more instances of the API. It is test support, not part of the deployable service; see [012](specs/012-aspire-test-environment.md).

## What starts

| Resource | What it is |
|---|---|
| `sql` / `Middleware` | SQL Server 2022 in a container with a generated password. The AppHost applies the shipped EF migrations once the database exists (the service itself never migrates at startup). |
| `simulators` | `tests/IPS.Middleware.Simulators`: IPS (`POST /Message`, answered ACCP or RJCT, signed with a throwaway key), the CBS status callback, and the Proxy Solution (`/PRX/register`, `/update`, `/remove`). TLS on one endpoint (a certificate generated for the run, trusted by the service through a file), plain HTTP on the other for health and the control API. |
| `middleware-1` ... `middleware-n` | The API, as a project or, with `Middleware:Container=true`, as a container built from `src/IPS.Middleware.Api/Dockerfile`. Each instance sends a different protocol version header so a test can tell who sent what. |

All service settings are injected as environment variables by `tests/IPS.Middleware.AppHost/Program.cs`; nothing is stored in the repository. The certificates are written to a temporary directory for the run.

## Run it

Docker must be running (Docker Desktop on Windows). From the repository root:

```
dotnet run --project tests/IPS.Middleware.AppHost
```

The dashboard URL is printed (set `ASPNETCORE_URLS`, `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL` and `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` for a run outside an IDE). Options, as arguments or configuration: `--Middleware:Instances=2` for two API instances sharing the database, `--Middleware:Container=true` to run the API from its image, `--Middleware:Concurrency=32` for the outgoing execution concurrency of each instance (default 4, a small limit so that instances share recovered work; the shipped default is 32), `--Middleware:Signing=true` to sign outgoing messages with a generated key instead of sending them unsigned (the simulated IPS does not verify the signature), `--Middleware:Timings=Shipped` to keep the timings of `appsettings.json` instead of the fast test timings (default `Test`: discovery every 200 ms, a 21-second HTTP wait and shorter budgets).

## Run the tests

```
dotnet test tests/IPS.Middleware.AspireTests
```

The tests start the same AppHost through `Aspire.Hosting.Testing`. They cover: ready and live, a pain.002 and a pacs.008 send with their callback, a rejecting IPS, a lost reply recovered by one flagged resend of the same bytes, the Proxy operations (accept and reject), validation errors that never reach the simulators, two instances sharing 40 payments, and the service running from its container image. Without Docker every Aspire test is skipped with the reason; the other test projects do not need Docker and still use LocalDB. The performance smoke test is skipped unless `IPS_PERF=1` is set (see below).

## Simulator control API

On the simulators' plain endpoint: `GET /_sim/received` (messages with the possible-duplicate flag, protocol version and receive time, callbacks with their body and receive time, core deliveries, proxy calls), `POST /_sim/behaviour` with any of `reject`, `rejectProxy`, `delayMilliseconds`, `block`, `loseNext`, `answerUnresolved`, `POST /_sim/release` and `POST /_sim/reset`.

## Measure performance

`tests/IPS.Middleware.Performance` measures the outgoing pacs.008 path on this stack against the owner's targets ([013](specs/013-measured-performance.md)). It starts the AppHost itself (two API instances as projects at the shipped execution concurrency and timings, signing on), sets the simulated IPS delay, warms up, sends on a fixed schedule round-robin over the instances, drains, checks what the simulators received and writes `docs/performance/<yyyy-MM-dd-HHmm>-baseline.md` (named by the start of the load, so a run never overwrites an earlier report). Besides the verdict per target, the report breaks the outcomes down by instance and by minute, lists the API's answers and the final statuses with their reason codes, and judges repeated callbacks by the service's delivery records (a repeat is allowed only after an unknown delivery outcome, [013a](specs/013a-performance-corrections.md)). Docker must be running; nothing else heavy should run on the machine during the measurement. The baseline takes about 15 minutes:

```
dotnet run --project tests/IPS.Middleware.Performance --configuration Release
```

Options, as `--Name=value`: `Rate` (requests per second measured, default 50), `Duration` (default `00:10:00`), `WarmUpRate` (default 10), `WarmUp` (default `00:01:00`, not measured), `Drain` (longest wait for the last callbacks, default `00:02:00`), `Instances` (default 2), `IpsDelay` (default `00:00:00.100`), `Output` (the report directory, default `docs/performance`), `Concurrency` (default the shipped value), `Timings` (`Shipped` by default; `Test` reproduces the 013 conditions), and `Label` (the report is `<yyyy-MM-dd-HHmm>-<Label>`, default `baseline`). For example `-- --Rate=10 --Duration=00:01:00 --Output=$env:TEMP\perf` for a short look. The exit code is 0 when every target is met, 1 when the report records a failed target (it says which and why) and 2 when the run is stopped with Ctrl+C, which still shuts the stack down.

For a quick check that the harness works, run it for 20 seconds at 10 requests per second into a scratch directory, as in the example above.

## Notes

The generated certificates and their passwords are written to a temporary directory for the run and deleted when the AppHost stops (the certificates are valid for a year, beyond the readiness check's 30-day expiry warning, so the stack reports Healthy); only the public certificates are mounted into a container. The passwords are visible in the Aspire dashboard and in `docker inspect`, which is acceptable for throwaway files. The API image is configured as Development by the AppHost and is meant for local and test runs only. The container build publishes the API on the host first, so it needs no NuGet access of its own (which also works behind a TLS-inspecting proxy).

## Limits

The environment proves behaviour against simulators, not a real IPS, CBS or Proxy Solution. The incoming receive path (long poll, acknowledgement, core submissions) is not simulated yet and is the first extension. Container startup takes tens of seconds and needs the SQL Server image and, for the container run, the ASP.NET 10 runtime image.
