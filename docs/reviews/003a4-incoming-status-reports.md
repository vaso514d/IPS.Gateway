# Review 003a.4: unsolicited incoming pacs.002

Branch codex/incoming-status-reports, base 3916554. On 2026-10-06 the owner approved the plan and decisions; the owner approved committing and merging on 2026-10-06; the commit containing this record was fast-forwarded into codex/capability-rebuild and the review branch is preserved. [Specification](../specs/003a4-incoming-status-reports.md).

## Delivered

A pacs.002 pulled from IPS about one of our outgoing payments is applied to that payment. A claimed pacs.002 receipt is routed by `IncomingReceiptPreparation` to the new `IncomingStatusReportProcessing`, which:
- reads the original message id as a lookup key;
- finds the payment by message id;
- verifies the report with the existing `IpsReplyInterpreter` (schema, trusted IPS signature, identifiers, status table);
- settles or observes the payment;
- completes the receipt.

Payment state, events, the CBS callback row and the receipt completion commit in one `UnitOfWork` save.

- **Settled:** Sending, Uncertain, Investigating, Resending → Accepted/Rejected, source Ips; scheduling and an expired claim are cleared; a payment under a live claim defers its receipt.
- **Observed only:** final, ManualReview and not-yet-sent payments record `payment.outcome-observed` / `payment.outcome-conflict-observed`, with no state change, callback, claim or schedule change.
- **Held:** an unverifiable, unmatched, unreadable or non-final report holds the receipt with its reason, and changes nothing.
- **Acknowledgement:** after the receipt commits, `IncomingReceiveWorker` sends `POST MessageAck` with the sequence header for pacs.002 receipts with a positive sequence, including duplicates. A failed ack is logged and never stops polling.
- **Domain:** Uncertain now permits Accept/Reject; `RecordReport` and `AwaitsOutcome` are new.
- **Removed or unchanged:** no Contracts, endpoint, JSON, schema or migration change; `has-pending-model-changes` reports none.

## Changed existing tests

- `OutgoingPaymentTests` transition table gains Uncertain → Accept/Reject (decision 3).
- `OutgoingTransactionIntakeTests.IntakeStorage` implements the new repository member.
- `IncomingCompositionTests` harness registers the status report protocol, because `IncomingReceiptPreparation` now depends on it (15 tests failed in the first full run until this was added).
- `IncomingTransportConfigurationTests` gains `AckPath` validation cases.

## New tests (26 cases in the status report class, 9 domain cases, 2 configuration cases)

Real SQL with independently Java-signed IPS replies:
- five active-state × status combinations, with callback, history and cleared scheduling;
- ten hold causes: PDNG, untrusted signature, unknown payment, wrong transaction id, wrong end-to-end id, wrong definition, schema-invalid (long reason), malformed, DTD payload, plus a group-level report accepted like a direct reply;
- four observation cases, and a waiting payment keeping its schedule;
- live-claim deferral then settlement;
- row-version fencing;
- atomic rollback after a failed commit, then resume;
- a duplicate sequence applied once;
- a worker that pulls, applies and acknowledges the report, with the ack failing and succeeding, a redelivery, and an unreadable zero-sequence report that is never acknowledged.

## Independent reviews

**Standards.** Real defects, all fixed:
- the report-key reader parsed unauthenticated XML with a DTD-permitting parser and now uses the shared safe reader (a DTD payload test holds);
- `StageSettlement` ran before deciding to settle, so an observation cleared a waiting payment's retry schedule; it now runs only for payments awaiting an outcome, and observation uses `StageObservation`;
- smaller clean-ups: `IsClaimLive` now underlies `HasLiveClaim`, one `Observe` helper in the domain, `IncomingIpsClient.SendAsync` takes the sequence before the cancellation token, and a worker blank line.

Declined: restructuring so the handler only returns a decision and `IncomingReceiptPreparation` completes the receipt. The handler owns the atomic commit like `IncomingPaymentIntake.RegisterAndReleaseAsync`; the duplicated hold helper is small. Also not added: a test through `IncomingWorkflowExecution` retry (the retry is existing, generic and unchanged).

**Spec.** One blocking defect, fixed: a hold reason longer than the 100-character column (any schema error) threw and left the receipt retrying forever, with IPS already acked. The journal now cuts the reason at its limit; the schema-invalid test covers it. Addressed: observation scheduling (above); group-level reports now documented and tested; missing coverage added (end-to-end id, definition, schema, DTD). Wording fixed in the spec and ledger.

## Verification

The last full run, on a machine running nothing else:

- `dotnet build` (Release): 0 warnings, 0 errors.
- **925 tests pass:** 266 unit/architecture/Contracts and 659 integration. The baseline was 888; 37 tests were added (9 domain, 26 status report, 2 configuration).
- `dotnet format` and the IDE0005 unused-usings check: clean.
- `dotnet ef migrations has-pending-model-changes`: no changes.

An earlier full run had 15 failures, all in `IncomingCompositionTests`, because its hand-built composition lacked the new status report protocol; the harness registration fixed them.

## Remaining

- **No real IPS verification.** Whether IPS accepts `MessageAck` for pacs.002 as the source sends it, and whether real reports verify under our stricter signature rule, are unconfirmed.
- **Message type detection.** The source recognised a report by body when the type header was missing; here such a receipt is held as unsupported.
- **Not covered by tests:** the `IncomingWorkflowExecution` retry path for this handler, a live claim held by a real investigation or resend (only a crashed Sending owner is exercised), and the ack ordering relative to receipt commit (it follows from `PersistReceiptAsync` completing first).
- **Carried open items:** three test-only production APIs; the unexplained intermittent `Pacs008ProcessingTests ("unsigned")` failure.
