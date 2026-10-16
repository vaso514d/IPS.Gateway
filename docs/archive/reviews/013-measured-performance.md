# Review 013: measured performance on the Aspire stack

Branch codex/payment-initiation, stacked on the 012c follow-up (2a553ca) and the unmerged slices before it; no merge is approved. On 2026-10-08 the owner approved the [specification](../specs/013-measured-performance.md) with:
- a 100 ms simulated IPS delay;
- a 10-minute window at 50 requests per second after a 1-minute warm-up at 10;
- two instances, run as projects;
- the report committed under `docs/performance/`.

The slice's deliverable is the measurement. The verdict is **fail**, and tuning is a separate decision. Commit approval is pending.

## Delivered

- **`tests/IPS.Middleware.Performance`**: a test-support console harness, never shipped and not referenced by `src/`.
  - **Stack and load.** It starts the AppHost and runs an open-model fixed-rate load, round-robin over the instances, recording how far each request starts behind its planned time.
  - **Latency.** Settlement runs from the send to the simulated core's final callback; response, from the send to the API's answer.
  - **Correctness.** Messages are classified by `MsgDefIdr`: pacs.008 sends, and pacs.028 investigations.
    - Accepted payments that were not sent, and lost ones, with no final outcome.
    - Duplicate sends and duplicate callbacks.
    - Status codes and response-body statuses.
    - Final statuses and reason codes.
  - **Health.** Readiness is sampled throughout. After the drain, the backlog gauge's own queries are run against the database.
  - **Diagnosis.**
    - A per-instance and per-minute breakdown.
    - The API instances' warning and error logs, grouped by message.
    - The machine, including the containers running beside the stack.
    - The settings that differ from `appsettings.json`.
  - **Report.** Markdown plus JSON. The exit code is 0 on pass, 1 on fail and 2 on cancel.
- **AppHost options** (the defaults keep the Aspire tests unchanged):
  - `--Middleware:Concurrency`, default 4; the harness uses the shipped 8.
  - `--Middleware:Signing`, default false; the harness signs. The certificate is generated for the run.
- **Test stack fix.** The generated certificates lasted 30 days, inside the shipped 30-day expiry warning, so every Aspire stack reported Degraded readiness. They now last 365 days.
- **Simulators.** Callbacks are recorded with their receive time.
- **Smoke test.** `PerformanceSmokeTests` is opt-in (`IPS_PERF=1`), skipped otherwise with its reason. It runs a 15 s warm-up and then 30 s at 10 requests per second on two signed instances, checking correctness only. Duplicate callbacks are reported but not asserted, because they are open (below).

## Baseline results

Both runs are on one developer machine, with an unrelated `kind` Kubernetes container also running.

| Target | Run 1 (unsigned, 08:03) | Run 2 (signed, 08:58, committed report) |
|---|---|---|
| Settlement p95 ≤ 1 s / p99 ≤ 2 s | 65.1 s / 85.5 s, **fail** | 558.6 s / 567.7 s, **fail** |
| Lost (no final outcome) | 0 (counted under "lost" then) | 13,285 still not final at the end of the drain, **fail** |
| Accepted, not sent to IPS | 2,106, all with a NotSent callback, **fail** | 22,389 (9,104 reported NotSent), **fail** |
| Duplicate sends | 0, pass | 0, pass |
| Duplicate callbacks | 7, **fail** | 9, **fail** |
| 5xx | 2,189 (504: 2,101, 500: 88), **fail** | 23,301 (504: 22,399, 500: 902), **fail** |
| Every response 200 or 504 | (not yet a target) | 902 answered 500, **fail** |
| Readiness Healthy | 270 of 270, pass | 287 of 322, **fail** |
| Generator lag p99 ≤ 50 ms | 15.8 ms, pass | 42.1 ms, pass |
| Work due after the drain | 0, pass | 202 callbacks, **fail** |

Run 2 is not directly comparable with run 1. Besides signing, its harness collected and parsed about 3.6 million log entries on the same machine, and the generator lag rose. Run 1 held for four minutes; run 2 collapsed in its first measured minute, on both instances alike.

## What the evidence shows

1. **Saturation.** One payment needs about 0.39 s (run 1 median), so 50 requests per second need about 20 in flight. Two instances at concurrency 8 allow 16. Up to 269 requests were open in run 1, and 1,084 in run 2.
2. **The submission deadline.** Every NotSent payment carries `TM01`/1015: it passed its 20-second submission deadline while waiting for admission. Its HTTP request then ended with 504 after the wait.
3. **Polling amplifies the load.** Each waiting HTTP request reads the payment's status every 100 ms, so database load grows with the queue.
4. **SQL failures under load (the 500s).**
   - 894 errors are EF Core's transient-failure wrapper and 7 are SqlException "A severe error occurred".
   - The logs show 30-second command timeouts on the outgoing recovery discovery query, a deadlock in callback discovery, and transaction errors.
   - Readiness turned 503 with "OutgoingRuntime is Stalled: No progress in: Outgoing recovery discovery".
5. **SQL command logging, a shipped configuration defect.** `appsettings.json` sets the default log level to Information and lowers only `Microsoft.AspNetCore`. So EF Core logs every SQL command, about 1.8 million log entries per instance in run 2, in production as well as here.
6. **Duplicate callbacks.** 7 and 9 payments received their Accepted callback twice, spread through each run, including run 1 before the collapse. Callback delivery is at least once, with an Idempotency-Key. Whether the core may receive such repeats, or this is a fencing defect under load, needs diagnosis and the owner's decision.

## Verification

- **Build and checks:** build 0 warnings; format and imports (IDE0005) clean; `git diff --check` clean.
- **Aspire tests:** 9 passed and 1 skipped (the smoke, with its reason), in 184 s.
- **Smoke:** with `IPS_PERF=1`, 3 of 3 passed (88 s, 103 s, 87 s), with 0 duplicate callbacks.
- **Baselines:** two full runs, each under `timeout 1500` and alone apart from the `kind` cluster; each wrote its report and exited 1 (fail).
- **Not run here:** the full suite, because the slice changes only test-support code, the AppHost options and the simulators. The integration tests do not use them.

## Independent reviews

**Standards.** No blocking findings.

Fixed:
- **pacs.028 crash.** A pacs.028 sent during a run would have crashed the report step. Messages are now classified by definition.
- **Flaky smoke.** The smoke test had no warm-up and asserted no duplicate callbacks.
- **4xx answers.** A 4xx answer passed every target; it now fails.
- **Signing.** Signing was not measured.
- **Overwritten reports.** A same-day run overwrote the committed report.
- **Rounding.** The verdict compared rounded values.
- **Small items.**
  - Ctrl+C now exits cleanly with its own code.
  - The readiness probe takes a cancellation token.
  - The `docker info` process is read without blocking and killed on cancel.
  - The backlog SQL uses the public enums.
  - The JSON report is readable.
  - The comment about the generator starting early is corrected.

Recorded:
- **Latency coverage.** Settlement percentiles cover only payments with a final callback; the lost row counts the rest.
- **Open connections.** The open-request count is a stand-in for busy execution slots.

**Spec.** No blocking findings; every Scope, measurement, Design and Acceptance item is implemented.

Fixed:
- **"Lost" was wrong.** The run-1 payments counted as lost had a final NotSent; the label is now "accepted, not sent", with "lost" kept for no final outcome.
- **The deadline cause was unproven.** It is now shown by the reason codes.
- **No per-instance evidence.** Outcomes are now broken down per instance and per minute, with the API logs.
- **Signing was unmeasured.**
- **The report could be lost.** A failing run could have ended without a report.
- **Missing evidence.** This file supplies it.
- **Stale ledger.**

Accepted deviations:
- **Backlog read from the database.** The backlog gauge is read with its own queries against the database, because the API has no metrics exporter.
- **Extra target rows.** Generator lag, work due after the drain, and every response 200 or 504.
- **Raw samples.** The per-payment samples are not committed.

## Limits

- **Indicative only.** One machine, simulators, the API as projects in Development.
- **Run 2 disturbed by its own measurement.** Log collection adds load to the machine. Future runs should make it cheaper or optional.
- **No tuning in this slice.** The proposed next slice (013a) is:
  1. lower EF Core command logging in the shipped configuration;
  2. diagnose the SQL timeouts and deadlock in discovery, and the duplicate callbacks;
  3. reduce or back off the status polling of waiting requests;
  4. size concurrency to the load;
  5. rerun the baseline.
