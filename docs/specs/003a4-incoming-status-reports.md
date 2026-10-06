# Review 003a.4: unsolicited incoming pacs.002

Status: on 2026-10-06 the owner approved the plan and decisions below. Implemented on codex/incoming-status-reports, base 3916554; the owner approved the commit and merge into codex/capability-rebuild on 2026-10-06. See the [review evidence](../reviews/003a4-incoming-status-reports.md).

## Scope

A pacs.002 that IPS sends on its own (not as the reply to our send) about one of our outgoing pacs.008 payments is applied to that payment: the payment outcome, its history, the CBS status callback and the receipt completion commit together. Nothing changes in Contracts, endpoints or JSON. Live processing stays disabled by default.

## Source evidence

Pinned source d498de6c4638aa71cdb20189d13642b41abab5f1, `Gateway/Services/IncomingStatusReportApplier.cs` and its tests:

- There is no HTTP endpoint. IPS messages are pulled with `GET /Message`; a pacs.002 arrives in the same loop, is registered in the receipt journal by (participant BIC, sequence), and is acknowledged with `POST /MessageAck` carrying `X-MONTRAN-IPS-MessageSeq` before business handling. A redelivery is re-acked through the duplicate path.
- Matching: the original message id (`OrgnlMsgId`) plus the original transaction id when present. No match, or an unmapped status, is silently ignored.
- Mapping: ACCP, ACSC and ACTC settle as Accepted; RJCT as Rejected, source Ips, with the first reason code and additional information. Any non-final payment is overwritten, scheduling is cleared and the CBS callback is requested. A final payment is left alone, logging only a conflicting report.
- The source verifies no signature, schema or sender, and never records a conflicting or ignored report.

## Owner decisions (2026-10-06)

1. **Verify like replies.** A report is applied only if it is schema-valid, signed by a trusted IPS certificate, of the exact pacs.002 definition and references the stored message id, transaction id and end-to-end id. Anything else holds the receipt and changes nothing. This is stricter than the source.
2. **Statuses.** The existing `IpsReplyInterpreter` is reused unchanged, so a direct reply and an unsolicited report always agree and match the source: ACCP/ACSC/ACTC accept, RJCT rejects. Other statuses (PDNG, ACSP, ACWC, …) are unresolved: the receipt is held with the reason and the payment is unchanged.
3. **Which payments change.** Sending, Uncertain, Investigating and Resending are settled. Final, ManualReview and not-yet-sent payments only record an observation event (`payment.outcome-observed`, or `payment.outcome-conflict-observed` when the reported outcome differs), with no state change and no callback. The state machine now permits Accept/Reject from Uncertain.
4. **Acknowledgement.** `POST {AckPath}` (default `MessageAck`) is sent after the receipt is durably committed, for pacs.002 receipts with a positive sequence. A failed acknowledgement never stops polling; the redelivery registers as a duplicate and is acknowledged again.

## Behavior

`IncomingReceiptPreparation` routes a claimed pacs.002 receipt to `IncomingStatusReportProcessing`:

1. Read the original message id as a lookup key only (`IStatusReportProtocol.OriginalMessageId`); unreadable → hold.
2. `IOutgoingPaymentRepository.FindByMessageIdAsync`; none → hold.
3. Correlate with the stored identifiers and verify/interpret through `IStatusReportProtocol.Interpret` (the existing interpreter); unresolved → hold with its description.
4. A payment awaiting its outcome is prepared with `ITransactionWorkRepository.StageSettlement`: under a live claim its owner is processing it, so the receipt is released again after `ContinuationDelay`; otherwise an expired claim and automatic scheduling are dropped and the parent row version is fenced. Any other payment only gets `StageObservation` (row-version fence), so its schedule and claim stay as they were.
5. `OutgoingPayment.RecordReport` settles or observes; the receipt completes; one `UnitOfWork` commit stores state, events, the callback row and the receipt result. Concurrent changes fail the version fence and the receipt is retried from SQL.

Held receipts are not scheduled again; they stay as evidence. The hold reason may carry protocol text (for example a schema error), so the journal cuts it at its stored limit of 100 characters instead of refusing it.

A report that names only the message (a group-level status with no transaction element) is applied on the message id, the original message definition and its signature, exactly like a direct reply to our send; the interpreter treats both alike on purpose.

## Verification

Real SQL and independently Java-signed IPS fixtures cover: each active state with each status, every hold reason, observation for final/ManualReview/Received, live-claim deferral, row-version fencing, atomic rollback after a failed commit, duplicate sequences applied once, and a worker test that pulls the report, applies it and acknowledges it (including a failing acknowledgement, a redelivery and an unreadable report without a sequence that is never acknowledged).

## Not in scope

Real IPS interoperability; the source's body sniffing to recognise a message type when the type header is missing (the receipt is held as unsupported); other message types (pacs.009/pacs.004, camt.056/camt.029).
