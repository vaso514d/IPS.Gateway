# Review 012a: business-flow audit corrections

Branch codex/payment-initiation, stacked on 012 (cc5de68) and the unmerged slices before it; no merge is approved. On 2026-10-07 the owner approved correcting the [audit](business-flow-audit-2026-10-07.md)'s production findings F1, S1 and F2 before 013 and approved the [specification](../specs/012a-audit-corrections.md). Commit approval is pending.

## Delivered

- **F1, acknowledgement outside the receive loop.** `IncomingReceiveWorker` commits a receipt, queues its MessageAck (`Acknowledgement` record: participant, sequence, type) on a bounded channel and polls again. A second loop in the same worker sends at most `AcknowledgementCapacity` (default 2) at once with the `work` token, beats "IPS acknowledgement" within one IPS request timeout, and after polling stops drains the queue within the shutdown budget. A full queue (`AcknowledgementBacklog`, default 1000) skips with a warning and the metric result `skipped`; IPS redelivery acknowledges the duplicate. The acknowledgement rule itself (types, positive sequence, no pacs.008) is unchanged. `IncomingWorkerOptions.IpsSendCapacity` (IPS ConnectionLimit - 1 - AcknowledgementCapacity) now sizes `IncomingReplyAdmission` (replacing composition's default in `AddIncomingWorkers`), the reply dispatcher and processing capacity, so replies and acknowledgements together stay within the reply pool; enabled workers without reply capacity fail validation at startup.
- **S1, fair CBS follow-up.** `FollowUpAdmission` gives each free slot to the next due, not-running item of the kind not admitted last (`FollowUpKind`), across sweeps; a kind with nothing due leaves its turn. `IncomingFollowUpWorker` keeps one instance and starts what it selects.
- **F2, pacs.004 ceiling.** `ValidatedPacs004.CurrencyOf`/`OriginalCurrencyOf` define the normalized currencies once; the snapshot and `Pacs004Validator`'s comparability check both use them. Different currencies are still not compared.
- `appsettings.json` (the two settings), `docs/configuration.md`, `docs/architecture.md`, the ledger.

## Tests

- **New:** `IncomingAcknowledgementTests` (real SQL, scripted receiver and acknowledgement client): ten or more receive calls while an acknowledgement is held, and each acknowledgement finds its receipt committed; a full queue skips sequence 3, keeps polling and records `skipped`; shutdown stops polling, then sends the queued acknowledgements; acknowledgements beyond the shutdown budget are abandoned and the worker stops cleanly; a hung acknowledgement is reported as a stall of "IPS acknowledgement" only while receive calls continue. The slow-acknowledgement test proves non-blocking by counting receive calls during the hold rather than by waiting five seconds. Against the previous receive worker four of the five fail (the abandon test passes on both).
- **New:** three `FollowUpAdmission` tests (a transfer admitted within two admissions under a replenished reconciliation backlog with capacity one; alternation as single slots free up and one kind alone filling every slot; running work skipped without using a slot or the turn), new validation cases (zero capacity or backlog, too few IPS connections), and four pacs.004 cases (empty, whitespace, lower-case, padded original currency; null was already covered).
- **Changed existing tests, none weakened:**
  - The IPS `ConnectionLimit` of three test hosts (`IncomingWorkerSqlTests`, `IncomingTransferTests`, `IncomingStatusReportTests`) is 5 instead of 3, so reply capacity stays 2 after the default acknowledgement reservation (3 would leave none).
  - `Handler_capacity_uses_both_connection_budgets` takes the acknowledgement capacity: (3, 5, 2) now reserves one acknowledgement, a (5, 5, 2, 2) row keeps the IPS-bound case, the (100, 100, 2) row expects 97 instead of 98, and the reply capacity is asserted.
  - `Worker_acknowledges_recalls_and_cancellations_...`: acknowledgements now run concurrently, so the simulator fails the first one to arrive by an atomic count, and the four acknowledged sequences are compared without order. The once-stored redelivery, the counts by type and the one `http_error` with three `ok` are unchanged.

## Verification

Build 0 warnings; format and imports (IDE0005) clean; no pending EF model changes. Full sequential run (`dotnet test IPS.Middleware.slnx -c Release --no-build -m:1`) after the review fixes: Aspire 9/9, integration 998/998 (989 before, plus nine new tests), unit 480/480 (476 before, plus four pacs.004 cases), no failures.

## Independent reviews

**Standards.** No blocking findings. Fixed: the recall acknowledgement test's order race; a host with workers disabled and an IPS connection limit of 2 (allowed by transport validation) would have crashed constructing the reply admission, now clamped to one (validation still refuses enabled workers without reply capacity); the admission registration no longer depends on call order (`Replace` after composition); the stall test now proves receive kept going; the `skipped` metric is asserted; the acknowledgement condition with a side effect is split into a named predicate; a fairness edge (a running head item passing the turn to the other kind) closed by selecting the next not-running item of the preferred kind; naming (`acknowledgement` parameter, `ReceiveLoop` constant, `FollowUpKind` instead of a boolean).

**Spec.** No blocking findings; every Design point and acceptance bullet matched. Fixed: the same recall test race and the `skipped` metric; the IPS connection sentence in `docs/configuration.md` (enabled workers with defaults need a limit of at least 4). Recorded: S1 is proved on `FollowUpAdmission`, not by a worker-level test (the worker wiring is one call); the reduced reply admission and reply dispatch capacity follow from "reserved the way receive already is" and are listed above; the pacs.008 no-acknowledgement assertion in `IncomingWorkerSqlTests` is now checked after asynchronous work, but the rule is unchanged and also covered by the type rule tests.

## Limits

Acknowledgement behaviour is proved against scripted clients and simulators, not a real IPS. A queued acknowledgement lost in a crash is left to IPS redelivery, as before. Two connections fewer are available for incoming replies at the default settings.
