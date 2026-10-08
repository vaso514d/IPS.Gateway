# Review 013a: correcting what the 013 baseline found

Branch codex/payment-initiation, stacked on 013 (2701c48) and the unmerged slices before it; no merge is approved.
- **Specification, 2026-10-08.** The owner approved the [specification](../specs/013a-performance-corrections.md), choosing:
  - at-least-once callbacks after an unknown outcome;
  - an in-process signal plus a backed-off poll for waiting requests;
  - no isolation change unless the diagnosis shows it;
  - the concurrency default from measurements.
- **Concurrency.** The owner then preferred concurrency 32, if the measurements allowed it.
- **Commit.** The owner approved the commit on 2026-10-08 and confirmed both points below.

## Delivered

1. **Logging.** `Microsoft.EntityFrameworkCore.Database.Command` is at Warning in the shipped `appsettings.json`, and a host test reads the shipped levels.
2. **Harness timings.** `--Middleware:Timings=Shipped` makes the harness use the shipped timings; the Aspire tests keep their fast ones. The harness gained optional log collection (off by default) and `--Diagnose`: Query Store, deadlock graphs, server waits and SQL sessions per process.
3. **Discovery queries.** The `DiscoveryIndexes` migration (EF CLI) adds a persisted dispatch priority and indexes that serve due-work and expired-claim discovery with no sort or lookup. Journal reads no longer use `TOP`, whose row goal made SQL Server scan the table. Tests cover the priority order, the generated SQL and the indexes.
4. **Deadlock and 500s.**
   - **The deadlock.** The captured graph was a key-lookup deadlock in callback discovery; a covering index fixes it. A test reproduces it with row versioning off.
   - **Transient failures at intake.** A transient SQL failure at intake answers 503 with `Retry-After: 1` and is logged at Warning. This covers a timeout, a deadlock victim, a lock timeout, a broken connection or an exhausted pool. Other SQL errors stay 500.
   - **Failed reads while waiting.** Once a payment is committed, a failed status read while waiting is only a missed poll.
5. **Waiting requests.** The instance that finishes an attempt signals its waiting request. Otherwise the request polls, backing off from 100 ms to 1 s, which halved the status reads. Tests cover the signal, the backoff and that nothing stays registered.
6. **Duplicate callbacks.** All five in the diagnosis followed an expired claim with an unknown outcome, so this is at-least-once delivery and not a fencing defect. The harness judges duplicates from the delivery records, which now keep the last failure.
7. **Concurrency 32.** It is the shipped default, measured against 8 and 16. No coupled limit had to change: the IPS and CBS connection limits are 100. The SQL connection pool guidance is in `docs/configuration.md`.
8. **Callback start (beyond findings 5 and 7, needs confirmation).** The callback of an attempt's outcome starts at once, instead of waiting for discovery. With shipped timings, discovery alone delivered 2.76 callbacks per second. A failed start is a Warning, and discovery delivers the callback.

## Measurements (50 requests per second, two instances, signed, shipped timings)

| Run | Settlement p95 / p99 | Not sent / 504 / 500 | Verdict |
|---|---|---|---|
| 3 min, concurrency 8 | 5.5 s / 45.9 s | 253 / 213 / 0 | fail |
| 3 min, concurrency 16 | 1.56 s / 12.2 s | 0 / 0 / 0 | fail |
| 3 min, concurrency 32 | 665 ms / 870 ms | 0 / 0 / 0 | **pass** |
| 10 min, 32, first run | 365.8 s / 496.4 s | 10,860 / 10,453 / 0 | fail |
| 10 min, 32, rerun after the journal fix | 587.9 ms / **2,361 ms** | 0 / 0 / 0 | fail on p99 only |

**Why the first 10-minute run failed.**
- **What the measurements show.** Query Store shows the journal reads as `Top > Clustered Index Scan`, at 20,000-26,000 logical reads each and about 18,900 s of SQL time. Both connection pools reached 100, and the rerun without `TOP` is clean.
- **What is not proven.** That the row goal is the mechanism, rather than plan instability: a unique filtered index matches the predicate, and the same statement was cheap in the shorter runs. The test checks the SQL text, not the plan.

**The rerun's p99** is not explained yet. It comes from one bad minute, by inference from the per-minute p95. The candidates are:
- lock waits;
- the callback overflow path, where a callback that finds no free slot waits for discovery. At least one callback arrived about 36 s after its final answer.
- log I/O;
- the `--Diagnose` overhead;
- the `kind` container sharing the machine.

The spec lists what would establish the cause. Row versioning is not proposed until a blocked-process report shows reader/writer blocking.

## Verification

- **Build and checks.** Build 0 warnings; format and imports clean; no pending EF changes after `DiscoveryIndexes`.
- **Unit tests.** 490/490.
- **Integration tests.**
  - The implementation's runs: 646 passed for the classes covering the changed code, then 477 for Payments and Transactions after the journal fix.
  - After the review fixes: `DatabaseFailureTests`, `OutgoingHostTests` and `OutgoingStatusDeliveryTests`, 71/71.
- **Aspire tests.** 9 passed and 1 skipped (the opt-in smoke).
- **Not run.** The full suite was not run again; the owner asked to stop long waits.

## Independent reviews

**Standards.** No blocking findings.

Fixed:
- **503 for every SQL failure.** Any SQL failure answered 503 and was not logged; now only transient ones do, logged at Warning.
- **503 after a committed intake.** A status-read failure after a committed intake answered 503 and lost the caller's wait.
- **Callback-start noise.** A failed callback start was logged as a failed payment.
- **The logging comment.** It claimed EF Core warns about slow commands.
- **Deployment note.** The migration rewrites and blocks `Transactions`.

Recorded:
- **Retries behind fresh callbacks.** Fresh callbacks take the callback slots first, so retries after a core outage drain at most 8 per 5 s per instance.
- **Metadata status copies.** The status copies on the metadata are read only by untracked discovery.
- **Deadlock test.** Its `ROLLBACK IMMEDIATE` drops pooled connections.

**Spec.** No code defects.

Fixed:
- **p99 explanation.** It was speculative; it is now stated as unknown, with the candidates.
- **Pool numbers.** They were out of date; they are now given as 79 of 100, with sizing guidance.
- **503 mapping.** Too broad, as above.
- **Docs.** The docs gaps are filled: pool sizing, the isolation difference between tests and deployment, and the ledger.

Recorded for the owner:
- **Beyond scope.** The immediate callback start goes beyond findings 5 and 7.
- **The workflow rule.** The journal fix was made after the failed baseline, although the rule is that further changes are proposed separately.
- **Duplicate check.** The harness's duplicate check flags "callbacks > attempts", which catches fencing defects rather than literally checking for an unknown outcome.
- **Delivery records.** Delivered rows now keep their last failure.

## Owner confirmations (given 2026-10-08 with the commit approval)

1. **Callback start.** Keep the immediate callback start after an attempt. Without it, callbacks reach about 2.8 per second.
2. **The journal fix.** Accept the journal fix made after the failed first 10-minute baseline.

## Limits

- **p99.** The 10-minute p99 target is not met (2.36 s), and its cause is open.
- **Indicative only.** One machine, simulators, the API as projects; an unrelated `kind` container was running.
- **Callback retries.** After a core outage, callback retries drain slowly.
