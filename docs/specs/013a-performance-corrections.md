# Specification 013a: correcting what the 013 baseline found

Status: specification on codex/payment-initiation, stacked on 013 (2701c48) and the earlier unmerged slices. On 2026-10-08 the owner asked for this slice after the [013 review](../reviews/013-measured-performance.md). The owner approved it on 2026-10-08, choosing the recommended option of each decision at the end:
- duplicate callbacks after an unknown outcome are at-least-once delivery;
- waiting requests get an in-process signal plus a backed-off SQL fallback;
- database isolation changes only if the diagnosis shows it is needed;
- the concurrency default comes from measurements at 8, 16 and 32.

Implemented and reviewed ([review evidence](../reviews/013a-performance-corrections.md)); the owner approved the commit and confirmed the immediate callback start and the journal fix.

The owner then stated a preference for 32 per instance: it becomes the shipped default if the 8/16/32 measurements show it is feasible (connection pool and connection limits allow it) and not worse than 16. Otherwise the evidence comes back for a decision.

## Scope

The 013 baseline failed: at 50 requests per second on two instances the service saturated, payments missed their submission deadline, SQL queries timed out and deadlocked, and a few callbacks were delivered twice. This slice diagnoses each cause with evidence, corrects the ones that are defects, and sets the configuration from measurements. It ends with a new baseline against the same targets. Each correction is proved by a test where one is possible, and by its effect in the harness otherwise.

Not in scope:
- **Production capacity planning.** Results stay indicative of one machine.
- **The incoming path.**
- **Changing the targets.** The targets stay as approved, except for the decision on duplicate callbacks below.

## Findings to correct, in order

1. **SQL command logging (a defect in the shipped configuration).**
   - The cause: `appsettings.json` keeps the default level at Information and lowers only `Microsoft.AspNetCore`. So EF Core logs every SQL command, about 1.8 million log entries per instance in the 013 run.
   - The correction: `Microsoft.EntityFrameworkCore.Database.Command` goes to Warning in the shipped configuration (failed commands stay visible at Error; EF Core logs no warning for a slow command).
   - The proof: a configuration test reads the shipped log levels.
2. **The harness ran with test timings, not shipped ones.**
   - The cause: the AppHost overrides timings for fast Aspire tests. The overrides are discovery every 200 ms (shipped 1-5 s), an HTTP wait of 21 s (shipped 30 s), an attempt budget of 22 s (shipped 35 s), and others. 013 used these.
   - The correction: the harness starts the stack with the shipped timings (a new AppHost option; the Aspire tests keep theirs). The report already lists every setting that differs from `appsettings.json`.
3. **Discovery queries that cannot use an index.**
   - The cause: outgoing recovery and due-work discovery order by an expression over a joined column ("pacs.008 first", then status time). Every sweep must sort every matching row; under load these queries timed out after 30 s and readiness reported the recovery loop stalled.
   - The diagnosis: capture each discovery query's plan and duration during a harness run (SQL Server's Query Store or the actual execution plan).
   - The correction: make the queries index-friendly. That means a stored priority, or an order an index serves, plus the indexes the plans call for, through an EF CLI migration.
   - The proof: a test asserting the generated SQL and the index exist, and the discovery durations in the harness report.
4. **The deadlock in callback discovery and the transient SQL failures (the HTTP 500s).**
   - The diagnosis: capture the deadlock graph (`system_health` extended events) during a run.
   - The corrections:
     - Fix the access order or the locking that produces the deadlock.
     - Intake must answer a transient SQL failure with 503 and `Retry-After`, never 500. The request is idempotent by client reference, so the core can repeat it safely.
   - The proof: a test that a transient failure at intake is 503, plus the harness's 500 count.
5. **Status polling by waiting requests.**
   - The cause: each HTTP request that waits for a final status reads it from SQL every 100 ms, so database load grows with the queue.
   - The correction (see decision 2): the instance that processes a payment signals its waiting request directly. SQL polling stays as a fallback for a payment finished by another instance, backing off from 100 ms to 1 s.
   - The proof: tests of both paths, and the harness's reduced query rate.
6. **Duplicate callbacks.**
   - The cause: 7 and 9 payments got their Accepted callback twice, including in the first four healthy minutes of run 1.
   - The diagnosis: each duplicate's delivery attempts, claims and commits, from the database and the logs. Is it at-least-once delivery after an unknown outcome (the core answered, our commit failed), or two owners delivering at once (a fencing defect)?
   - The correction: a fencing defect is fixed and proved by a two-owner test. Delivery after an unknown outcome is governed by decision 1.
7. **Concurrency sized from measurement.** With corrections 1 to 5 in place, run the harness at concurrency 8, 16 and 32 per instance (shorter runs, 3 minutes each). Then propose the shipped default from the results, with the database connection pool and the IPS connection limit checked against it. The default changes only with the owner's approval of that evidence.
8. **The harness's own load.** Log collection becomes optional and is off by default. When on, it keeps only warning and error lines. Then a baseline is not disturbed by its own measurement.

## Acceptance

- **Corrections proved.** Each correction above has its proof, and no existing test is weakened. Changed expectations are listed in the review.
- **The final baseline.** The full baseline (signed, two instances, shipped timings, the chosen concurrency, log collection off) is rerun once and its report committed. The verdict follows the 013 targets and decision 1.
- **If a target still fails,** the report and the review say why, and any further change is proposed separately.
- **Verification.** Build, format and imports, the EF check (a migration is expected for the indexes), and the affected test classes. The full suite runs once in the background with a time limit. Two independent Standards and Spec reviews.

## Owner decisions (2026-10-08; the first option of each was chosen)

1. **Duplicate callbacks after an unknown outcome:**
   - Accept them as at-least-once delivery (recommended). The core already receives the same Idempotency-Key and must deduplicate. The target becomes "no duplicate callback without an unknown delivery outcome before it", and the harness checks it from the delivery records.
   - The alternative is a hard zero. It is not achievable over HTTP without the core's help, because an answer can always be lost after the core acted.
   - A fencing defect, if found, is fixed either way.
2. **Waiting requests:**
   - An in-process signal plus a backed-off SQL fallback (recommended).
   - The alternative is backoff only, which is simpler but leaves more database reads.
3. **Database isolation:**
   - Do not change it in this slice unless the deadlock diagnosis shows reader/writer blocking as the cause (recommended).
   - The alternative is to require READ_COMMITTED_SNAPSHOT on the database now. That is a deployment requirement the service cannot set itself, because it never migrates or configures its database at startup.
4. **The concurrency default:**
   - Decide from the 8/16/32 measurements, presented for approval (recommended).
   - The alternative is to keep 8 and scale by instances only.

## Risks

- **Not the real IPS.** The simulators answer after a fixed delay; real IPS latency variance will change the numbers.
- **Machine-bound diagnosis.** Query plans on a developer SQL Server container may differ from production. The indexes are justified by the queries' shape, not only by this machine.
- **Duplicate callbacks may remain.** If they stem only from unknown outcomes under load, they will not disappear entirely; decision 1 defines what the target means then.

## Implementation notes

Part 1 (diagnosis, corrections, the concurrency measurement) and part 2 (the final baseline) were implemented on 2026-10-08. Uncommitted; review pending.

### The harness and the AppHost

- **AppHost `Middleware:Timings`.** `Test` (the default, so the Aspire tests keep their fast timings) or `Shipped`, which stops overriding the IPS request timeout, the HTTP wait, the attempt budget, the pacs.008 ownership and the three discovery intervals.
- **Harness options.**
  - `--Timings`: Shipped by default; Test reproduces the 013 conditions.
  - `--Concurrency`: the shipped value by default.
  - `--CollectLogs`: off by default; when on, only warning, error and critical entries are counted and grouped.
  - `--Diagnose`: Query Store on the run's database, capturing every statement from an empty store. After the drain the report lists:
    - the 15 costliest statements by total duration, with executions, failed (aborted) executions, mean, max, CPU, logical reads, wait categories, text, and a summary of the plan each ran with most often;
    - the deadlock graphs that the `system_health` event files recorded since the load started (summarised in Markdown, whole in JSON);
    - the server's waits over the run;
    - the peak SQL sessions per client process, sampled every 2 s.
  - `--Label`: names the report files.
- **Process names.** The orchestrator reports the process of `dotnet run`, not the API process SQL Server sees, so the connection peaks name processes by id.
- **Duplicate callbacks from the delivery records.** After the drain the harness reads every `OutgoingStatusDeliveries` row with its payment's client reference. A payment with more callbacks than claimed attempts in its records fails the target "no duplicate callback without an unknown delivery outcome before it" (decision 1). Each claimed attempt calls the core at most once, so more callbacks than attempts means a call without its own claim.
  - The report shows both counts: payments with more than one callback, and those without an unknown outcome before the repeat.
  - The opt-in smoke test is unchanged. It now fails on an unexplained repeat only, which is decision 1's target.

### Diagnosis (before the corrections)

Two runs of 3 minutes at 50 per second, two signed instances, concurrency 8, `--Diagnose --CollectLogs`, with the binaries before the corrections (the logging change was already in `appsettings.json`):
- shipped timings: report (removed from the tree; in git history at 2190e81);
- the AppHost's test timings of 013: report (removed from the tree; in git history at 2190e81). This run was added because the first showed neither the deadlock nor a 500.

| | Shipped timings | Test timings (013) |
|---|---|---|
| Responses | 9,600 × 200 | 9,357 × 200, 232 × 504, 11 × 500 |
| Not sent (NotSent `TM01`/1015) | 51 | 282 |
| Callbacks delivered | 1,088 of 9,600 (2.76 per second); 8,512 pending after the drain | all |
| Settlement p95 / p99 | 286 s / 291 s (488 samples) | 17.1 s / 23.9 s |
| Payments with more than one callback | 0 | 5 |
| Deadlocks | 0 | 2 |
| Readiness | Healthy throughout | Healthy throughout |
| Warnings and errors logged | none | 24 (11 transient-failure 500s, 4 callback discovery deadlock victims, 5 transaction errors) |

**Finding 3, discovery queries.**
- **The plan.** Outgoing due-work discovery (`FindDueAsync`, `FindExpiredAsync`) joined Transactions to itself (EF Core's table splitting: the claim columns belong to the metadata, the status to the payment) and ordered by `CASE` over `MessageType`. The plan sought the status index, sorted every row with that status, then looked up each row: *Index Seek > Compute Scalar > Sort > Clustered Index Seek*.
- **Shipped timings:** 1,552 executions, mean 9.1 ms, max 441 ms, 28 logical reads, 13.9 s of lock waits.
- **Test timings:** 6,099 executions, mean 13.5 ms, max 1,437 ms, 52 logical reads, 80.8 s of lock waits.
- **The waits are locks.** The reader waits for the keys and rows of payments being claimed. Under the 013 meltdown these waits became the 30-second timeouts.
- **The backlog gauge.** Its outgoing count scanned the whole table (`NextActionAtUtc` had no index): 6,028 logical reads per execution.
- **Status reads.** The waiting requests' status read (by client reference) was the busiest statement: 64,349 executions in the shipped run (37.8 s of lock waits) and 177,373 with test timings.

**Finding 4, the deadlock and the 500s.**
- **Two deadlock graphs** (09:57:48 and 09:57:56), both on `OutgoingStatusDeliveries`, the classic lookup deadlock:
  - callback discovery held S on its key in `IX_OutgoingStatusDeliveries_State_NextAtUtc_PaymentId_Sequence` and waited for S on the row in `PK_OutgoingStatusDeliveries`, a lookup for `ClaimToken`/`ClaimExpiresAtUtc`;
  - a delivery finish (`UPDATE … SET State, NextAtUtc, DeliveredAtUtc`) or an abandoned-claim recovery (`SET NextAtUtc, LastFailure`) held X on the row and waited for X on the index key it moves;
  - SQL Server chose discovery as the victim (logged as "Outgoing callback discovery failed").
- **Reader/writer blocking.** The deadlock comes from the plan's lock order, which an index removes, so the isolation level stays unchanged (decision 3).
- **The 500s.** All 11 were EF Core's transient-failure wrapper around a SqlException. The 013 baseline also had 7 "A severe error occurred" and 1 "Operation cancelled by user", which are reads cancelled by the HTTP wait.

**Finding 5, waiting requests.** Covered by the status reads under finding 3.

**Finding 6, duplicate callbacks.**
- **The records.** All 5 repeats in the test-timings run were "Delivered after 2 attempts, last failure: Previous delivery owner expired; CBS receipt is unknown."
- **The cause.** The delivery finish UPDATE has 5 failed executions with a maximum of 2,004 ms: the 2-second `StatusDelivery:PersistenceBudget` cancelled finishes that were waiting for locks. So the core answered and our commit failed. The claim expired after its 45-second ownership, recovery recorded the unknown outcome, and the next attempt delivered again.
- **Verdict.** This is at-least-once delivery after an unknown outcome (decision 1). The fencing held: no payment had more callbacks than attempts, so there is no two-owner defect. The existing two-owner test `Competing_claims_have_one_committed_owner` stays as it is.

**A new finding: callback dispatch was bounded by its discovery.**
- **The cause.** With the shipped timings, callback discovery runs every 5 s. Each sweep starts at most `CallbackConcurrency` (8) deliveries per instance, and the rest of its batch waits for the next sweep. That caps callbacks at 8 per 5 s per instance; the shipped-timings run delivered 2.76 per second against 50 outcomes per second.
- **Why 013 missed it.** With the test timings (a 200 ms sweep) it stayed hidden.
- **The correction.** It is outside the approved list: the owner should confirm it. See correction 5.

### Corrections and their proof

1. **SQL command logging.**
   - **Change:** `Microsoft.EntityFrameworkCore.Database.Command` is at Warning in `appsettings.json`.
   - **Test:** `HostTests.Shipped_logging_writes_sql_commands_only_from_warning_and_keeps_the_service_at_information` checks Development and Production.
   - **Effect:** the before runs logged no info line to parse.
2. **Shipped timings.** The harness runs `Middleware:Timings=Shipped`, and the report's settings table no longer lists timing overrides.
3. **Index-friendly discovery** (migration `DiscoveryIndexes`, generated with `dotnet ef migrations add`).
   - **Read-only status copies.** `OutgoingPaymentMetadata` gains `CurrentStatus` and `CurrentStatusAtUtc`, read-only copies of the payment's columns. Their save behaviour is Ignore, and both mappings name the columns explicitly, which EF Core requires to share a column (as RowVersion already is). The migration adds no column for them.
   - **Stored priority.** It also gains `DispatchPriority`, a persisted computed column: `CASE WHEN [MessageType] = N'pacs.008' THEN 0 ELSE 1 END`.
   - **The query.** Discovery now reads one alias: `SELECT TOP(@p) [t].[Id] FROM [Transactions] AS [t] WHERE … ORDER BY [t].[DispatchPriority], [t].[CurrentStatusAtUtc], [t].[Id]`, the same order as before (pacs.008 first, then the oldest status change).
   - **Indexes:**
     - `IX_Transactions_CurrentStatus_DispatchPriority_CurrentStatusAtUtc_Id` INCLUDE (`ClaimToken`, `NextActionAtUtc`) replaces `IX_Transactions_CurrentStatus_MessageType_CurrentStatusAtUtc_Id`. The old index existed for the old order; the investigation and resend discovery still seek its leading `CurrentStatus`.
     - `IX_Transactions_ClaimExpiresAtUtc` gains INCLUDE (`ClaimToken`, `DispatchPriority`, `CurrentStatusAtUtc`).
     - `IX_Transactions_NextActionAtUtc` is new, filtered to `[NextActionAtUtc] IS NOT NULL`, for the backlog gauge.
   - **Proof.** `AggregateOwnershipTests.Discovery_keeps_pacs008_first_then_oldest_through_the_stored_priority_that_the_status_index_orders` checks:
     - the order: a pacs.008 that is older, then a newer pacs.008, then an older pacs.009;
     - the SQL: the `ORDER BY`, no `CASE`, no `JOIN`;
     - the key and included columns of the three indexes, the persisted computed column and the filter.

     The existing priority test is unchanged and passes.
   - **Effect at concurrency 8.** The plan is *Top > Compute Scalar > Index Seek* (no sort, no lookup, no self-join), at 5 logical reads instead of 28. The remaining time is lock waits on keys of payments being claimed, still under locking read committed: mean 9.0 ms, max 588 ms.
   - **The journal reads without TOP.** The first final baseline (below) showed a second hot statement: four single-row reads of `OutgoingMessages` used `SingleOrDefault` in SQL, so `TOP(2)`. Once the journal had grown, the row goal of `TOP` made SQL Server scan the whole journal for the one row:
     - the 32 run: 9,639 reads per preparation read;
     - the first baseline: both reads by payment at about 200 ms and 20,000 to 26,000 reads per execution.

     On a scratch LocalDB journal grown from 3 to 131,000 rows, the `TOP(2)` form read 25,000 to 39,000 pages per call from about 2,000 rows on, and the same statement without `TOP` about 130. `OutgoingJournal.Single` now reads the key's rows without `TOP` and picks the one in memory, for `OutgoingJournal.Find` and for the investigation and resend message lookups; the preparation read does the same. The proof is `OutgoingJournalTests.Processing_reads_the_journal_without_top`, which checks that every journal read of a processed payment is free of `TOP`.
4. **The deadlock and the 500s.**
   - **The deadlock.** `IX_OutgoingStatusDeliveries_State_NextAtUtc_PaymentId_Sequence` gains INCLUDE (`ClaimToken`, `ClaimExpiresAtUtc`), so callback discovery reads the claim from the index and no longer looks up the row.
     - **Proof:** `OutgoingStatusDeliveryTests.Discovery_does_not_deadlock_with_a_delivery_that_holds_its_row_and_then_moves_its_index_entry` reproduces the graph's lock order.
     - **The old index fails it:** with the old index recreated in the test database, SQL Server chose the test's discovery as the deadlock victim after about 5 s. With the new index it passes in 2 s.
     - **No deadlock** occurred in the three concurrency runs.
   - **Row versioning in the test databases.** EF Core creates its test databases with `READ_COMMITTED_SNAPSHOT` on, so no SQL test could see reader/writer blocking. The Aspire database, and a deployment's (the service never configures its database), use locking read committed. The new test switches row versioning off for its own database. Running every SQL test that way is proposed separately.
   - **Intake 503.**
     - **Change:** `OutgoingPaymentsController` answers a SqlException anywhere in the exception chain (which covers EF Core's transient wrapper) at the six send endpoints with 503, `Retry-After: 1` and a problem body. `DatabaseFailure.IsSqlFailure` (Infrastructure) classifies it.
     - **Proof:** `OutgoingHostTests.A_transient_sql_failure_at_intake_answers_503_with_retry_after_and_the_repeated_request_is_accepted`. A table lock and a 1-second command timeout produce exactly the 013 wrapper; the test checks the 503 with `Retry-After`, then that the repeat is answered 200 Accepted with one IPS send.
   - **The HTTP-wait read.** `OutgoingSubmission` now treats any failure of a read that its own HTTP-wait deadline cancelled as the deadline (504 with the last committed status), because SqlClient can report the cancellation as an error.
     - **Proof:** `OutgoingSubmissionTests.A_read_failing_because_the_http_deadline_cancelled_it_answers_the_committed_intake_as_timed_out`.
   - **Effect:** no 500 in any run after the corrections.
5. **Waiting requests and callbacks.**
   - **The signal.** `OutgoingAttemptSignals` (Application, a singleton) holds one completion per payment that a request waits for. `OutgoingSubmission` registers before admission, waits for the signal or the poll, and always removes the entry. `OutgoingRuntime` signals after every attempt's last commit, whatever its outcome.
   - **The fallback poll** reads SQL first after `StatusPollInterval` (100 ms), doubling up to the new `StatusPollMaxInterval` (1 s, shipped, which must not be shorter). The first read no longer happens immediately after intake.
   - **The callback start (deviation; for the owner's confirmation).** After the signal, the runtime reads the due delivery of the payment's committed outcome and starts it at once in a callback slot. Callback discovery remains for retries, for outcomes committed by another instance and when no slot is free.
   - **Proof:**
     - `OutgoingSubmissionTests.An_attempt_finished_on_this_instance_wakes_the_waiting_request_which_reads_the_outcome_once`: a 30-second poll, answered within 5 s with one read;
     - `…A_payment_finished_by_another_instance_is_found_by_the_poll_backing_off_to_its_longest_interval`: waits of 100, 200 and 200 ms with a 50 to 200 ms backoff;
     - every case asserts that no signal entry remains, after timeout and cancellation too;
     - end to end, `OutgoingHostTests.The_attempt_on_this_instance_answers_the_waiting_request_and_starts_its_callback_without_polling_or_discovery` uses a 10-second poll and 60-second callback discovery, and the answer and the callback arrive within seconds.
   - **Effect.**
     - Status reads at concurrency 8: 29,731 instead of 64,349 in the same 3 minutes; 2.2 per payment at 32.
     - Callbacks: 49.3 to 49.9 per second in every concurrency run, nothing pending after the drain.
6. **Duplicate callbacks.**
   - **No fencing defect** (see the diagnosis).
   - **Evidence kept.** `OutgoingStatusRepository.StageFinishAsync` now keeps the last failure when a later attempt delivers, so the record says why the earlier attempt is unknown.
     - **Proof:** `OutgoingStatusDeliveryTests.A_delivery_after_an_abandoned_attempt_keeps_the_unknown_outcome_on_record`.
   - **The harness target** is decision 1's.
   - **The cause of the 5 repeats** was locks holding the finish past its persistence budget, and the covering index removes that blocking. No repeat occurred in the concurrency runs.
7. **Concurrency.**
   - **The comparison.** See the [comparison](../performance/2026-10-08-1015-concurrency.md): 32 meets every target, 8 and 16 queue. SQL sessions peaked at 23 to 40 per API process (Max Pool Size 100). Admission stays within the IPS (100) and CBS (100) connection limits.
   - **The default.** The owner chose 32 if feasible, so `appsettings.json` ships `Concurrency` 32. No coupled limit changes: investigations and resends run inside the same slots, and callbacks keep their own 8. The options' built-in default stays 8.
   - **Docs.** `docs/configuration.md` is updated.
8. **The harness's load.** Log collection is optional and off by default (above).

### Changed existing tests

- **`OutgoingSubmissionTests.Slow_sql_observation_is_cancelled_at_http_deadline_and_returns_committed_intake`** sets a 10 ms first poll. The first read now waits for the first poll, and the test still needs a read in flight at its 100 ms deadline. It also asserts that no signal entry remains.
- **Constructor and composition updates for the runtime's new dependency.** The intent is unchanged:
  - `OutgoingHostTests.Stop_after_dispose_is_safe_and_does_not_admit_work`;
  - `ValidationRejectionMetricTests`;
  - the other `OutgoingSubmissionTests`;
  - `OutgoingProcessProbe`, which registers `OutgoingAttemptSignals`.
- **`AggregateOwnershipTests.Intake`** takes an optional intake time.

### Open items

- **Callbacks after an outage.** Retries still drain at `CallbackConcurrency` per discovery interval per instance (8 per 5 s), so a CBS outage of a minute at 50 per second takes about 15 minutes to deliver. A sweep that refills slots as they free is proposed separately.
- **Lock waits under locking read committed.** Discovery and status reads still wait for keys and rows of payments being written (at concurrency 32, 12.0 s and 33.1 s over the run, no timeout). Decision 3 keeps the isolation, because nothing failed. `READ_COMMITTED_SNAPSHOT` would remove these waits, but it is a deployment setting the service cannot make.
- **SQL tests and row versioning.** As noted under correction 4, EF Core creates the SQL test databases with row versioning, unlike the deployment's database.

### Part 2: the final baseline (concurrency 32, 10 minutes)

The owner chose 32 if feasible, so the baseline ran at 32. Settings: 50 per second, two signed instances, shipped timings, log collection off. `--Diagnose` was on, for comparability with the concurrency runs and to explain a failure.

| Target | First run, 11:04 (report (removed from the tree; in git history at 2190e81)) | Rerun after the journal fix, 11:32 ([report](../performance/2026-10-08-1132-baseline.md)) |
|---|---|---|
| Settlement p95 / p99 | 365.8 s / 496.4 s, **fail** | 587.9 ms pass / 2,361.1 ms **fail** |
| Lost (no final callback) | 5,662, **fail** | 0, pass |
| Accepted, not sent | 10,860 (5,891 reported NotSent), **fail** | 0, pass |
| Duplicate callbacks without an unknown outcome | 0, pass | 0, pass |
| 5xx | 10,453 (all 504), **fail** | 0, pass |
| Readiness | 326 of 326, pass | 266 of 266, pass |
| Work due after the drain | 5,509 callbacks, **fail** | 0, pass |
| Verdict | **fail** | **fail** (p99 only) |

**The first run failed.**
- **Callbacks and payments.** After the 2-minute drain, 5,509 callbacks were still waiting and 153 payments were not final. 5,662 accepted payments had no final callback. 10,860 never reached the IPS, 5,891 of them reported NotSent at the 20-second submission deadline.
- **The minutes.** The first measured minute was bad and the next two recovered. Then, from the fourth minute, 1,200 to 1,800 requests per minute answered 504. At most 1,031 requests were open at once.
- **The cause, from the diagnosis.** Both `TOP(2)` reads of `OutgoingMessages` by payment ran as scans for the whole run:
  - 59,449 executions at a mean of 215 ms and about 20,000 logical reads;
  - 30,807 executions at a mean of 198 ms and about 25,800 reads;
  - together about 18,900 s of SQL time.
- **The server.** SOS_SCHEDULER_YIELD reached 5,338 s and LCK_M_S 10,147 s. Both API processes held their full SQL pool of 100 connections. The attempts then outlasted their slots, queued work missed the submission deadline, and callbacks queued behind the same database.
- **Confirmation.** The scratch reproduction (correction 3) confirms the row goal. The rerun, with every journal read free of `TOP`, has no such statement.
- **The order of work.** The fix was made before the owner's instruction to stop changing code. It is the same "hot query the diagnosis shows" as step 5, and it is proved by its test and the rerun.

**The rerun fails only p99.**
- **The tail.** Settlement p50 303.5 ms, p95 587.9 ms, p99 2,361.1 ms, max 41.8 s. The tail comes from one minute (11:36:31, p95 4,782 ms); every other minute's p95 was 344 to 592 ms. At most 126 requests were open, and 502 requests started with all 64 slots busy.
- **The cause is not established.**
  - **Lock waits are only one candidate.** Lock waits under locking read committed are possible: due-work discovery waited up to 3.86 s, and the status reads accumulated 186 s of lock waits. But these are run-wide totals, and nothing names a blocker for the 11:36 minute.
  - **The overflow path fits the data.** The slowest response took 5.8 s, but the slowest settlement took 41.8 s, so at least one callback arrived about 36 s after its final answer. That fits the overflow path, where a callback that finds no free slot waits for discovery (every 5 s, at most 8 per sweep), better than delayed admission.
  - **Other candidates:**
    - log I/O (`PREEMPTIVE_OS_FLUSHFILEBUFFERS` 3,111 s, `WRITELOG` 1,320 s);
    - the overhead of `--Diagnose` (Query Store captures every statement);
    - the unrelated `kind` container on the same machine.
  - **Inferred, not shown.** The report gives only p95 per minute, so "the tail comes from one minute" is inferred, not shown.
- **What would establish it, not yet measured:**
  - a rerun without `--Diagnose`, with per-minute p99 and callback-delay figures;
  - a `blocked_process_report` for a bad minute, naming the blocking statement.

  Row versioning (`READ_COMMITTED_SNAPSHOT`) is not proposed until such a report shows reader/writer blocking. It would be a new owner decision: decision 3 was about the deadlock, which the covering index fixed.
- **No further change is made** (the owner's instruction). Options are proposed separately: row versioning on the database, fewer status reads (a later first poll), or a longer `Ownership`/claim design review.

## Review fixes (2026-10-08)

- **503 only for transient failures.** `DatabaseFailure.IsTransient` admits command timeouts, deadlock victims, lock request timeouts, broken, refused or throttled connections (listed SQL error numbers, or severity 20 and above), and an exhausted connection pool. Any other SQL error stays a 500, so the core is never told to repeat a request that cannot succeed. The 503 is logged at Warning. A test covers the classification.
- **A failed status read after intake is a missed poll.** Once the payment is committed, a read that fails while the request waits (a deadlock victim, a timeout) no longer answers 503. The next poll or the end of the HTTP wait decides, and the failure is counted in `ips.errors`.
- **Callback start.** A failed lookup when the callback starts at once is logged at Warning, not as a failed payment. Discovery delivers the callback.
- **Connection pool.** The 3-minute runs peaked at 23-40 SQL sessions per process. The 10-minute rerun peaked at 79 of the default Max Pool Size of 100, leaving 21% headroom; the first 10-minute run exhausted it. Pool use grows with waiting requests and lock waits, not with `Concurrency`. `docs/configuration.md` gives the sizing guidance.
- **Deployment.** The `DiscoveryIndexes` migration adds a stored computed column and rebuilds indexes on `Transactions`. It rewrites the table and blocks it while it runs, so apply it in a maintenance window.
- **Isolation.** Deployed databases run under locking read committed. The SQL test databases use row versioning, so only the deadlock test (which turns it off) sees reader/writer blocking.
- **Owner confirmations.** The owner is asked to confirm two things:
  - the immediate callback start after an attempt, a dispatch change beyond findings 5 and 7;
  - that the journal fix was made after the failed first baseline, against the rule that further changes are proposed separately.

## Harness slimmed afterwards (2026-10-08)

The `--Diagnose` and `--CollectLogs` options used for this slice's diagnosis were removed from the harness at the owner's request; they remain in history at 2190e81. The runs recorded above were made with them.
