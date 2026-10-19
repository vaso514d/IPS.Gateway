# Local environment (Aspire)

A whole deployment of the middleware on one machine, for local runs and for the Aspire tests: SQL Server in a container, simulators standing in for IPS, the CBS and the Proxy Solution, and one or more instances of the API. It is test support, not part of the deployable service; see [012](archive/specs/012-aspire-test-environment.md).

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

`tests/IPS.Middleware.Performance` measures the outgoing pacs.008 path on this stack against the owner's targets ([013](archive/specs/013-measured-performance.md)). It starts the AppHost itself (two API instances as projects at the shipped execution concurrency and timings, signing on), sets the simulated IPS delay, warms up, sends on a fixed schedule round-robin over the instances, drains, checks what the simulators received and writes `docs/performance/<yyyy-MM-dd-HHmm>-baseline.md` (named by the start of the load, so a run never overwrites an earlier report). Besides the verdict per target, the report breaks the outcomes down by instance and by minute, lists the API's answers and the final statuses with their reason codes, and judges repeated callbacks by the service's delivery records (a repeat is allowed only after an unknown delivery outcome, [013a](archive/specs/013a-performance-corrections.md)). Docker must be running; nothing else heavy should run on the machine during the measurement. The baseline takes about 15 minutes:

```
dotnet run --project tests/IPS.Middleware.Performance --configuration Release
```

Options, as `--Name=value`: `Rate` (requests per second measured, default 50), `Duration` (default `00:10:00`), `WarmUpRate` (default 10), `WarmUp` (default `00:01:00`, not measured), `Drain` (longest wait for the last callbacks, default `00:02:00`), `Instances` (default 2), `IpsDelay` (default `00:00:00.100`), `Output` (the report directory, default `docs/performance`), `Concurrency` (default the shipped value), `Timings` (`Shipped` by default; `Test` reproduces the 013 conditions), and `Label` (the report is `<yyyy-MM-dd-HHmm>-<Label>`, default `baseline`). For example `-- --Rate=10 --Duration=00:01:00 --Output=$env:TEMP\perf` for a short look. The exit code is 0 when every target is met, 1 when the report records a failed target (it says which and why) and 2 when the run is stopped with Ctrl+C, which still shuts the stack down.

For a quick check that the harness works, run it for 20 seconds at 10 requests per second into a scratch directory, as in the example above.

## Connect to the IPS UAT

IPS authenticates the bank by its TLS client certificate and answers every request without one with 401, so the gateway refuses to start without it. The `Uat` launch profile, the Api's only profile, points both directions at `https://ips.uat.ipsgeorgia.ge:444/ips` (the gateway calls `.../ips/Message` and `.../ips/MessageAck`), enables outgoing execution, incoming transport and the incoming workers, and names every certificate by thumbprint alone.

| Certificate | Thumbprint | Setting (both directions) |
|---|---|---|
| `IT-IPSClientAuthTest` (TLS client, ECC, with private key) | `B264BEB0329F4CDB55CFC84E12558CE7A9075A72` | `Ips:ClientCertificate` |
| `IT-IPSSignAgentTest` (XML signing, ECC, with private key) | `D93CFF446F02C2B265DEB9FDF39D4E3F622F868F` | `SigningCertificate` |
| `IPS Class 1 Root CA` | `D7EBAB3464BF9F04FC84EBF37ED6E445CFCDD1BD` | `Ips:ServerTrust:0` |
| `IPS-Class2-SubCA-Test` | `59A0E74A883DF309BEF5A27190788945BEFCB7C2` | `Ips:ServerTrust:1` |

- **Certificate stores.** A thumbprint is looked up in the Personal, Root and Intermediate stores of the current user first, then of the local machine. A development machine therefore installs the certificates for its user, and a server installs them in `LocalMachine` with the same configuration. On a server the account that runs the gateway needs read access to the two private keys (certlm.msc → the certificate → All Tasks → Manage Private Keys → Read); without it startup fails with `Keyset does not exist`.
- **IPS server certificate.** The UAT server certificate is issued by IPS's private CA, which the operating system does not trust, so `Ips:ServerTrust` names both CAs. The profile turns off the IPS revocation check, because the CRLs are published only in IPS's internal LDAP (`DC=ips,DC=local`). Hostname and chain validation stay on.
- **Development.** The profile sets `ASPNETCORE_ENVIRONMENT=Development`. Use it on a test machine only: Development also serves `/openapi/v1.json`.
- **No IPS signature checks.** `IpsSignatureTrust` stays empty until IPS provides its signing certificate, so IPS signatures are not checked.
- **Core system.** The CBS base URL is `http://localhost:5280`, the sample receiver (`dotnet run --project samples/IPS.MiidleWear.CoreReceiver.Sample`), which stands in for the core system until a real one is connected.
- **From appsettings.json.** The bank codes and the SQL connection string come from `appsettings.json`; put a different connection string or password in user secrets or the environment, not in the file.
- **Database.** `Database:MigrateOnStartup` stays off, so apply the migrations to the UAT database with the EF CLI first.
- **Proxy.** Proxy management stays disabled until NBG gives its address.

### In a Linux container

A container has no certificate store, so the same settings name files instead of thumbprints: a `.pfx` (certificate and private key) takes `Path` and `Password`, a `.cer` (public CA) takes `Path` only. [docker-compose.yml](../src/IPS.Middleware.Api/docker-compose.yml) runs the image against the UAT: it mounts `src/IPS.Middleware.Api/certs` (ignored by git) at `/certs` and reads the two PFX passwords, the connection string and the CBS URL from a `.env` file next to it. The key stays in memory; nothing is written to disk. The container runs as Production, so the CBS URL must be HTTPS and every message is signed.

## Notes

The generated certificates and their passwords are written to a temporary directory for the run and deleted when the AppHost stops (the certificates are valid for a year, beyond the readiness check's 30-day expiry warning, so the stack reports Healthy); only the public certificates are mounted into a container. The passwords are visible in the Aspire dashboard and in `docker inspect`, which is acceptable for throwaway files. The API image is configured as Development by the AppHost and is meant for local and test runs only. The container build publishes the API on the host first, so it needs no NuGet access of its own (which also works behind a TLS-inspecting proxy).

## Limits

The environment proves behaviour against simulators, not a real IPS, CBS or Proxy Solution. The incoming receive path (long poll, acknowledgement, core submissions) is not simulated yet and is the first extension. Container startup takes tens of seconds and needs the SQL Server image and, for the container run, the ASP.NET 10 runtime image.
