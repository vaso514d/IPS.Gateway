# Review 008c: incoming camt.055 (acknowledge and archive)

Branch codex/payment-initiation, stacked on 008b (1f2926f) and the unmerged slices 005a-005d and 007a-007c; no merge is approved. On 2026-10-07 the owner approved the [specification](../specs/008c-incoming-camt055.md). Commit and merge approval are pending.

## Delivered

- `PaymentMessageTypes` gains `Camt055`, `Camt055Definition` (`camt.055.001.12`) and named constants for the registry spellings `.012` and `.08`. The 007c rule is renamed `IsArchivedCancellation` and names recalls, refusals and cancellations once, so `IncomingReceiptPreparation` (archive after the sequence check) and `IncomingReceiveWorker` (acknowledge after the commit) change only by the rename.
- No Contracts change, migration or endpoint; nothing is verified, delivered to the core or answered.

## Changed existing tests

- Additive only: camt.055 rows in the held-without-sequence and archive theories (all four spellings), a camt.055 delivery in the worker acknowledgement test (its expected statuses and acknowledgements gain the new sequence), and two test names reworded (`...recalls_and_cancellations...`, `A_recall_or_cancellation...`).

## Verification

Build clean; format and imports checks clean; targeted tests 30/30; full suite result recorded in the commit message of the owner-approved commit (see the presentation).

## Independent reviews

**Standards.** No blocking findings. Fixed: stale "recall" comments in `IncomingReceiptPreparation`, the long worker comment, the registry spellings as named constants, test names, and the ledger placement and link.

**Spec.** No deviation from the spec or the source. Recorded: the worker test uses only the short `camt.055`; duplicate-sequence, non-positive-sequence and the other spellings reach the same predicate and are covered through the composition tests and the camt.056 worker cases.

## Limits

As in 007c: no verification, the core is not told, and that IPS wants only an acknowledgement rests on the source. A PISP's cancellation of an initiation we hold is archived; closing that needs a Contracts receive method and an owner decision.
