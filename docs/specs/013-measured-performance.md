# Specification 013: measured performance on the Aspire stack

Status: specification on codex/payment-initiation, stacked on the 012c follow-up (2a553ca) and the earlier unmerged slices. On 2026-10-07 the owner set the targets:
- at most 50 requests per second;
- at least two instances;
- p95 at most 1 s and p99 at most 2 s, measured against the simulators;
- no lost or duplicated payment over a sustained run.

On 2026-10-08 the owner approved this specification with the recommended option of each decision at the end: a 100 ms simulated IPS delay, a 10-minute measured window, the API run as projects, and the report committed under `docs/performance/`. Implemented on 2026-10-08 (see the implementation notes and the baseline (removed from the tree; in git history at 2190e81)); awaiting review and the owner's approval. Implemented and reviewed ([review evidence](../reviews/013-measured-performance.md)); the baseline verdict is fail, and commit approval is pending.

## Scope

A repeatable measurement of the outgoing payment path under the owner's load, run on the 012 Aspire stack: SQL Server in a container, the simulators, and two or more API instances sharing the database. It produces a report, and it decides pass or fail against the targets. Measuring is the deliverable. Any tuning a failed run calls for is a separate, owner-approved change, not part of this slice.

Not in scope:
- **The incoming path.** It is not simulated yet (012 limits).
- **Proxy calls.**
- **Real-world latency.** Real IPS latency, a production network, and production hardware. Results come from one developer machine and are indicative, not a capacity guarantee.
- **Load-balancer behaviour.** The generator spreads requests over the instances itself.
- **Running in the normal suite.** The measurement takes minutes and depends on the machine, so it does not run as part of `dotnet test` on the solution.

## What is measured

- **Workload.** Outgoing pacs.008 sends: the main payment, and the path with the most work (signing, SQL markers, the IPS call, the reply interpretation and the callback). Every request has a unique client reference, and the requests are spread round-robin over the instances.
- **Load shape.** Open model: requests start on a fixed schedule whatever the latency, so a slow service cannot quietly lower the load.
  - Warm-up: 1 minute at 10 requests per second, not measured.
  - Measured window: 10 minutes at 50 requests per second.
  - Drain: up to 2 minutes for the outstanding callbacks.
- **Latency.** For each payment:
  - **Settlement latency** runs from when the generator sends the request until the simulated core receives that payment's callback. The targets apply to this, because it is when the core knows the outcome.
  - **Response latency** runs from send to the API's HTTP response. It is reported as well.
  - Both times come from one clock, since the generator and the simulators run on the same machine. The simulators gain a receive time on each recorded callback.
- **Correctness**, checked over everything sent:
  - every accepted payment reaches the simulated IPS, and has exactly one callback with its final status;
  - no payment is sent twice except as a flagged possible-duplicate resend of the same bytes;
  - no 5xx response, and no 504 under the load;
  - readiness stays Healthy on every instance throughout;
  - after the drain, the `ips.backlog.due` gauges are back to zero.
- **Simulated IPS.** The simulated IPS answers accepted, after a fixed delay (default 100 ms; see the open decisions) so the IPS round trip is not zero.

## Design

- **Harness.** A test-support console project, `tests/IPS.Middleware.Performance`, never shipped and not referenced by `src/`. It starts the AppHost with `Aspire.Hosting.Testing` (`--Middleware:Instances=2`, more on request) and runs the load. It needs Docker, like the Aspire tests.
  - **Options:** rate, durations, instance count, IPS delay, and the output path.
- **Load generator.** Plain .NET, with no new package. A fixed-rate scheduler starts each request at its planned time and records any lag behind the plan, so the generator itself is proved not to have fallen behind. Latencies are kept as raw samples, and percentiles are computed exactly.
- **Report.** A Markdown and JSON report under `docs/performance/` is committed with the slice as the first baseline. It contains:
  - the targets and the result (pass or fail per target);
  - p50, p95, p99 and maximum for both latencies;
  - achieved throughput and generator lag;
  - the correctness counts and the readiness samples;
  - the machine (CPU, memory, OS, Docker resources) and the configuration used.
- **Simulator change.** Callbacks are recorded with their receive time and exposed by `/_sim/received`. Existing tests read the callback body; they keep working.
- **Service configuration.** The service runs with the AppHost's existing settings, with one exception. Execution `Concurrency` is raised from the AppHost's test value of 4 to the shipped default of 8, so the run measures a deployable configuration. The report lists every setting that differs from `appsettings.json`.
- **Opt-in smoke test.** The Aspire tests gain one short opt-in test (`IPS_PERF=1`): 30 seconds at 10 requests per second on two instances, checking correctness only. It proves the harness end to end without making the normal suite slower or machine-dependent. By default it is skipped, with its reason.

## Acceptance

- **The baseline run.** One full run at 50 requests per second on two instances completes and writes the report. The generator's lag is small enough to show it held the rate: p99 lag at most 50 ms.
- **The verdict.** The report's verdict follows the targets exactly:
  - settlement latency p95 at most 1 s, and p99 at most 2 s;
  - zero lost payments, zero duplicate sends, zero duplicate callbacks, and zero 5xx responses;
  - readiness Healthy throughout.
- **A failing run.** If a target fails, the report says which, with the evidence. The slice is still complete as a measurement, and the follow-up tuning is proposed separately for approval.
- **The smoke test.** It passes three times in a row when enabled, and is skipped when not.
- **Verification.** Build, format and imports, the affected test classes, the full suite once (`-m:1`, in the background, with a time limit), and independent Standards and Spec reviews.

## Owner decisions (2026-10-08; the first option of each was chosen)

1. **Simulated IPS delay:** 100 ms fixed (recommended): a plausible round trip that keeps the measurement about our service. The alternatives are 0 ms, measuring the service alone, or a randomised 50 to 300 ms.
2. **Measured window:** 10 minutes at 50 requests per second, 30,000 payments (recommended). The alternative is 30 minutes, which is a better soak but makes each run long.
3. **API image:** run the API as projects (recommended; faster to iterate) or as containers from the Dockerfile (closer to deployment, with slower startup). The report records which was used.
4. **Report location:** commit the report under `docs/performance/` as the first baseline (recommended), or keep reports out of the repository.

## Risks

- **Measuring the machine.** One machine runs the containers, both instances, the simulators and the generator. Contention can inflate latencies, and the report's machine section is there to judge that.
- **Not the real IPS.** The simulators sign with the service's own signer and answer instantly apart from the configured delay. Real IPS behaviour and signature verification time are not represented.
- **Concurrency headroom.** Two instances at the shipped concurrency of 8 can work on 16 payments at once. At 50 requests per second with a 100 ms IPS delay, signing and SQL, about 15 payments are expected in flight. The run may therefore show the concurrency limit, not the code, as the bottleneck; the report says so if the queueing appears.
- **Timeouts.** The DCP one-minute container timeout seen in 012 can affect stack startup on a loaded machine; the harness starts the stack before any load.

## Implementation notes

- **Harness.** `tests/IPS.Middleware.Performance` is a console project in `IPS.Middleware.slnx`. It references `Aspire.Hosting.Testing`, the AppHost, Contracts, and Domain, Application and Infrastructure (for the database context and the stored enums); no package was added.
  - **Options.** `RunOptions` takes `--Rate`, `--Duration`, `--WarmUpRate`, `--WarmUp`, `--Drain`, `--Instances`, `--IpsDelay` and `--Output`, with the owner's values as defaults.
  - **Exit code.** 0 when every target is met, 1 when the report records a failure, 2 when the run is stopped with Ctrl+C. A stopped run still shuts the stack down.
  - **HTTP clients.** Plain, with no retry or resilience handler, so each request reaches the service once.
- **Concurrency and signing.** The AppHost gained two options, both with defaults that leave the Aspire tests unchanged:
  - `Middleware:Concurrency` (default 4). The harness reads the shipped value from `src/IPS.Middleware.Api/appsettings.json` (8) and passes it.
  - `Middleware:Signing` (default false). It configures a generated ECDSA P-256 key with digital-signature usage as `Payments:Outgoing:Transport:SigningCertificate` and turns `AllowUnsignedInDevelopment` off. A container run mounts that key at `/signing`. The harness always signs, because a deployment does. The simulated IPS does not verify our signature and still answers, as the smoke test and the baseline show.
- **Settings in the report.** The report lists the settings that differ from `appsettings.json`, taken from the orchestrator's record of each instance's environment. The connection string and the passwords are masked, and temporary paths are shortened. Aspire's own `Logging:Console:FormatterName` appears there too.
- **Simulator change.** Callbacks are recorded as a body with its receive time, and `/_sim/received` returns them as `{ body, receivedAtUtc }`. The Aspire tests read callbacks only through `Stack`, which now reads `body`; no test changed.
- **Clock.** The generator stamps each send with `DateTimeOffset.UtcNow`, and the simulators stamp each receipt the same way on the same machine. Lag and response latency use `Stopwatch`. Task.Delay wakes on the system timer (about 15 ms on Windows), so a request can start up to one tick late, or a fraction of a millisecond early, which shows as a negative lag.
- **Matching.** The simulated IPS's messages are sorted by `AppHdr/MsgDefIdr`:
  - a pacs.008 is a send of the payment named by its `EndToEndId`;
  - a pacs.028 is an investigation of the payment named by its `OrgnlEndToEndId`;
  - anything else, or unreadable XML, is counted as other, by definition.

  Callbacks and the API's answers are read in full (status, reason code, IPS internal code). An unreadable body is counted, never fatal, so a run always ends with a report.
- **The backlog gauge is read from the database.** The service publishes `ips.backlog.due` only inside its own process (there is no metrics exporter), so the harness cannot read the gauge. After the drain, it runs the five due counts of `BacklogReader` against the database through the AppHost's SQL connection string. The drain waits until no outgoing payment is non-final and no callback is pending. The service's database sets are internal, so these are SQL queries whose enum values come from the public `TransactionStatus`, `InboundProcessingStatus` and `StatusDeliveryState`.
- **Verdict rules.** The verdict is the conjunction of every target row, and it compares unrounded values.
  - **Accepted:** answered 200 or 504.
  - **Accepted, not sent:** an accepted payment that never reached the simulated IPS, or whose final callback is NotSent. It fails its target, because the specification wants every accepted payment at the IPS, even though the core was told.
  - **Lost:** an accepted payment that has no callback with a final status.
  - **Zero 5xx:** also requires that every request got a response.
  - **Every response is 200 or 504:** a separate target, failed by a 4xx or anything else.
  - **No work due after the drain:** a target row, because the specification lists it with the correctness checks.
- **Evidence for a failure.** The report contains:
  - the first five payments with each problem;
  - the API's answers by status code and body (for a 500 in Development, the first line of the exception page);
  - the final statuses with their reason and IPS internal codes;
  - a breakdown by instance (responses, accepted-not-sent, lost, duplicate callbacks, first failure);
  - a per-minute timeline.
- **API logs.** `InstanceLogs` follows each instance's console log through `ResourceLoggerService`, keyed by the running instance's resource id. It skips the replayed lines written before the load, by line number, and removes the orchestrator's timestamp and the console colours. It counts entries by level, and groups warnings and errors by a normalised template (category and first message line, at most 80 characters) with the first entry and its first exception line. The shipped logging configuration writes every SQL command at Information, about 50 entries per payment, which the report's level counts show.
- **Readiness defect in the test stack.** The first smoke run found every instance answering `/health/ready` with 200 `Degraded` for the whole run. The AppHost's throwaway certificates were valid for 30 days, inside the shipped 30-day `Diagnostics:CertificateWarning`, so every Aspire stack had been Degraded; the 012 tests check only the status code, which is 200 for Degraded too. The generated certificates now last 365 days. Neither the service nor its settings changed. The harness counts a sample as Healthy only when it is 200 with the text `Healthy`.
- **Smoke test.** `IPS.Middleware.AspireTests` references the Performance project (internals visible to it). The smoke test runs the whole harness, writes the report to a temporary directory and deletes it.
  - **Load:** a 15-second warm-up (not measured), then 30 seconds at 10 per second on two instances.
  - **Asserts:** no anomaly other than a duplicate callback; every response 200; nothing lost, left unsent or sent twice; a completed drain with nothing due; readiness Healthy on both instances; signing on.
  - **Duplicate callbacks:** written to the test output but not asserted. Callback delivery is at least once, with the idempotency key `<clientReference>:<status>`, and whether that meets "exactly one callback" is open for the owner's decision in the next slice.
  - `PerformanceFactAttribute` skips it without `IPS_PERF=1` and reuses the Docker skip reason.
- **Report files.** Named `<yyyy-MM-dd-HHmm>-baseline.md` and `.json` by the start of the load, so a later run never overwrites an earlier one. The JSON uses a relaxed encoder, holds every reported value and every readiness sample, and leaves out the raw per-payment samples.
- **Cold start.** Trial runs at 10 per second showed the first seconds after the stack starts are slow: the settlement p95 of the first 10 seconds was about 1 s. The 1-minute warm-up covers that; the correctness counts include the warm-up payments.
- **First run (before the review fixes, report replaced).**
  - **Not met:** settlement p95 65.1 s and p99 85.5 s (p50 389 ms); 2,106 accepted payments never reached the IPS, all reported NotSent; 2,189 5xx (2,101 were 504, 88 were 500); 7 duplicate callbacks.
  - **Met:** lag p99 15.8 ms, readiness and nothing due.
  - That run was unsigned and had no breakdowns or logs. The reviews asked for the evidence above, and the run below replaces it.
- **Baseline: fail** (report (removed from the tree; in git history at 2190e81), 2026-10-08 08:58 UTC, signed, 10 minutes at 50 per second on two instances).
  - **Met:** no duplicate send; generator lag p99 42.1 ms (max 655 ms).
  - **Settlement:** p95 558.6 s and p99 567.7 s. Only 16,202 of the 30,000 measured payments settled before the drain ended.
  - **Lost:** 13,285 payments got no callback within the drain, which ran out with 13,589 payments not final.
  - **Not sent:** 22,389 accepted payments never reached the IPS; 9,104 of them were already reported NotSent `TM01`/1015, the submission deadline.
  - **5xx:** 23,301 (22,399 were 504, 902 were 500).
  - **Other failures:** 9 duplicate callbacks; 35 of 322 readiness samples answered 503 Unhealthy, on both instances, from 09:03; 202 callbacks still due after the drain.
  - **When and where:** both instances failed alike, from 08:59:56, 23 seconds into the measured window, so this is no single-instance stall. The first measured minute already had 956 answers of 504.
  - **Cause, from the evidence:** up to 1,084 requests were open against 16 execution slots. Each waiting request polls its status every 100 ms (`StatusPollInterval`), and the database became the bottleneck:
    - 894 of the 500s are EF Core's "transient failure" wrapper, and 7 more are a SqlException ("A severe error occurred");
    - the logs show 30-second command timeouts on the recovery discovery query, a deadlock in callback discovery and transaction errors;
    - readiness turned Unhealthy because "OutgoingRuntime is Stalled: No progress in: Outgoing recovery discovery".

    The service also logs every SQL command at Information: 1.8 million info entries per instance during the run, which costs CPU on the shared machine.
  - **Not isolated:** this run collapsed in its first measured minute, where the first run held for four. Besides signing, the harness now parses every log line of both instances (3.6 million entries), the generator lag p99 rose from 15.8 to 42.1 ms, and a `kind` cluster was running on the machine. The two runs therefore do not isolate the cost of signing.
- **Machine.** The harness, the AppHost orchestration (and its proxies), both API processes, the simulators and the SQL Server container share one machine, as the risks say. The report lists every container running at the start of the load; on the developer machine an unrelated `kind` cluster was running and was left alone.

## Slimmed after 013a (2026-10-08)

At the owner's request, the harness was cut from about 2,350 to about 1,530 lines.

- **Removed:**
  - the machine description and the per-process SQL connection probe;
  - the API log collection (`--CollectLogs`) and the database diagnosis (`--Diagnose`: Query Store, deadlock graphs, waits);
  - the JSON report;
  - the opt-in smoke test (`IPS_PERF=1`) with its attribute and the Aspire test project's reference to the harness.
- **Kept:**
  - load, latency and correctness, including the duplicate check against the delivery records;
  - readiness and the backlog after the drain;
  - the per-instance and per-minute breakdown;
  - the settings that differ from `appsettings.json`;
  - the Markdown report.

The removed code stays in history at 2190e81 for a future diagnosis. The report no longer has a machine section, a deviation from the Design above.
