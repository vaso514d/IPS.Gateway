# Specification 012a: business-flow audit corrections

Status: specification on codex/payment-initiation, stacked on 012 and the earlier unmerged slices (merge approval pending for all of them). On 2026-10-07 the owner approved correcting the three production findings of the [business-flow audit](../reviews/business-flow-audit-2026-10-07.md) in one slice before 013; the owner approved this specification on 2026-10-07; implemented and reviewed ([review evidence](../reviews/012a-audit-corrections.md)), commit approval pending. The audit's test-environment finding S2 was fixed in 012.

## Scope

Three corrections, each with a proving test, and nothing else:

- **F1.** The incoming receive loop must not wait for a MessageAck before its next receive call.
- **S1.** CBS follow-up must admit pacs.008 reconciliation and transfer work (pacs.009, pacs.004, pain.001) fairly across sweeps.
- **F2.** An outgoing pacs.004 must not return more than the stated original amount when the original currency is blank or spelled differently.

Not in scope: the audit's recorded limits (certificate expiry during verification, unsigned Proxy messages, recall completion, unsupported incoming types, simulator coverage), which are approved decisions or separate slices. No Contracts change, no endpoint, no migration.

## F1: acknowledgement outside the receive loop

**Defect.** `IncomingReceiveWorker` commits a receipt and then awaits `AcknowledgeAsync` before its next GET. A slow or lost acknowledgement holds the loop for the IPS `RequestTimeout` (20 seconds by default), during which this instance makes no receive call. Annex D 2.2 (page 18) expects a receive call at least every 5 seconds for the participant's online status. Specifications 003a4, 005c, 007c and 008c require that acknowledgements never block polling. The source (d498de6c, `IpsInboundReceiverService.cs`) receives in one loop and acknowledges from a separate processing pool; the rebuild merged the two in 003a4.

**Design.**

- The receive loop commits the receipt as today, then hands the receipts that need a MessageAck (unchanged rule: pacs.002, incoming transfers, archived cancellations, with a positive sequence) to a bounded in-process queue, and polls again at once.
- An acknowledgement loop inside the same worker drains the queue with at most `AcknowledgementCapacity` acknowledgements at a time (default 2). Acknowledgement still happens only after the commit, and pacs.008 still gets none (its pacs.002 reply confirms it).
- When the queue is full (`AcknowledgementBacklog`, default 1000), the acknowledgement is skipped with a warning and the `ips.incoming.acknowledgements` result `skipped`. The receipt is already stored, IPS redelivers an unacknowledged message, and the duplicate is acknowledged again, so skipping loses nothing and never blocks polling.
- **Shutdown.** Polling stops first, then the queued acknowledgements are drained within the worker's shutdown budget. Whatever the budget cuts off is redelivered by IPS. Both loops are awaited by the worker, so nothing runs untracked.
- **Readiness.** The acknowledgement loop reports its own heartbeat, so a hung acknowledgement shows as a stalled "IPS acknowledgement" loop without delaying the "IPS receive" heartbeat.
- **Connections.** Acknowledgements use the existing IPS reply client. `AcknowledgementCapacity` is reserved from the IPS connections the way receive already is: processing capacity becomes `min(IPS ConnectionLimit - 1 - AcknowledgementCapacity, CBS ConnectionLimit - CbsFollowUpCapacity)`, and configuration fails at startup if nothing is left.
- **Configuration.** Both settings sit under `Payments:Incoming:Workers`, are validated as positive and are documented in `docs/configuration.md`.

Considered and rejected: recording acknowledgement state in SQL and acknowledging from a recovery sweep. IPS redelivery already makes an in-memory queue safe after a crash, and a durable variant adds a migration for no behaviour the redelivery does not already give.

## S1: fair CBS follow-up

**Defect.** Each discovery sweep in `IncomingFollowUpWorker` starts with pacs.008 reconciliation work. With `CbsFollowUpCapacity=1`, or with one slot freeing at a time, a steady reconciliation backlog takes every free slot, so due transfer work never starts and can pass its reconciliation deadline. The interleaving only alternates within a sweep. Specification 005c's delivery, recovery and settlement acceptance, shared by 005d and 008a, needs transfers to progress.

**Design.** The worker remembers which kind it admitted last and starts the next admission with the other kind, across sweeps. When one kind has nothing due, the other takes every free slot as now. An id that is already running does not count as admitted. Capacity limits, the separate scopes and SQL ownership are unchanged.

## F2: return amount ceiling with the normalized currency

**Defect.** `Pacs004Validator` compares the raw `Original.Currency` with `Currency` to decide whether the amounts are comparable. A blank, whitespace-only or untrimmed original currency fails that comparison, so the ceiling is skipped. Normalization (`ValidatedPacs004`) then defaults the blank to the returned currency. Example: a 200 GEL return against a stated 100 GEL original is accepted and sent. Specification 005b requires that the amount does not exceed the stated original. The audit calls this inherited, but the source refused it later: its domain mapped the blank to null, defaulted it, and the `Pacs004PaymentReturn` constructor rejected a larger same-currency return (d498de6c, `Domain/Iso20022/Pacs004PaymentReturn.cs` line 116). The rebuild kept the first check and lost the second, so this is a regression.

**Design.** The validator decides comparability with the same trimming, upper-casing and default as the normalized snapshot, through one shared definition in `ValidatedPacs004`, so the two can no longer disagree. Genuinely different currencies are still not compared, as the source and 005b intend.

## Acceptance

**F1**
- An acknowledgement delayed beyond 5 seconds does not delay the following receive calls (worker test with a scripted receiver and an acknowledgement client that blocks).
- Acknowledgement still follows the commit.
- The existing tests keep passing: a failed acknowledgement does not stop polling, a redelivery is acknowledged again, a duplicate receipt, no acknowledgement for pacs.008, unsupported types or non-positive sequences, and shutdown.
- A full queue skips the acknowledgement and keeps polling.
- Shutdown drains queued acknowledgements within the budget.
- A blocked acknowledgement makes readiness report the acknowledgement loop as stalled while receive stays healthy.
- The new settings are validated, and the shipped configuration starts.

**S1**
- With capacity 1 and a continually replenished reconciliation backlog, due transfer work is admitted within two sweeps.
- With a larger capacity and one slot freeing at a time, both kinds keep being admitted.
- Either kind alone still uses the whole capacity.

**F2**
- An original currency that is null, empty, whitespace, lower-case or padded rejects a return larger than the original amount.
- A different original currency still skips the comparison.
- The normalized snapshot is unchanged.

**All**
- No existing test is weakened. Tests whose expectations change because of these corrections are listed in the review.
- Build, full tests (`-m:1`), format and imports, the EF check (no model change expected), and independent Standards and Spec reviews.
