# Review 007a: outgoing camt.056 (request for recall)

Branch codex/recalls, stacked on codex/incoming-pacs004 (b2e090f) and the unmerged slices 005a-005c; no merge is approved. On 2026-10-06 the owner approved the slicing (outgoing camt.056 first), the HTTP convention (200 or 504, with the stale 202 declarations corrected) and the reason-code rule (the source pattern, no lookup), then the [specification](../specs/007a-outgoing-camt056.md). Commit and merge approval are pending.

## Delivered

- **Intake and validation.** `Camt056Request`, a validator that ports every source rule (ids, enabled original currency, amount, settlement dates required and not in the future, reason code `^[A-Z0-9]{1,4}$`, debtor and creditor with name, IBAN checksum, optional postal address and identifier, ultimate parties, agents, remittance, and the debtor agent being our participant), `ValidatedCamt056`, `AcceptedCamt056` and `Camt056Intake`. The caller chooses the message id (`Id`) and the recall id; both must be unused and the unique indexes fence a race. The settlement dates are judged against the current UTC date, as the source does.
- **XML.** `camt.056.001.11` (`FIToFIPmtCxlReq`) in the IPS v1 profile: assignment from our participant to IPS, one transaction with the recall id, the original group (name id fixed to `pacs.008.001.12`), ids, amount and date, the reason with our BIC as originator, and the original transaction reference. Validated against the embedded schema (byte-identical to the source's) and signed like the other types.
- **Reply correlation.** Our message id with the recalled payment's transaction and end-to-end ids and `camt.056.001.11`; the status shows the original end-to-end id. "Accepted" is IPS's technical verdict only: the business answer arrives later as a pacs.004 or camt.029 and is not tracked, as in the source.
- **API and recovery.** `POST /api/ips/camt056/send` returns 200 with the final status or 504 with the current status; duplicates return the current status at once; unknown outcomes are resent as possible duplicates exactly as for pacs.009 and pacs.004.
- **Contracts metadata (approved).** `SuccessStatusCode` and the description of `SendPacs009Async`, `SendPacs004Async`, `SendCamt056Async` and `SendCamt029Async` now say 200 (final) or 504, in the wording of pacs.008. `public-api.json` was edited explicitly for those four methods, the baseline README records the exception, and no type or wire shape changed. `SendPain002Async` stays 202 until it is built.
- **Shared profile.** The identical `Pacs009ProtocolProfile` and `Pacs004ProtocolProfile` became one `IpsMessageProfile` when camt.056 would have been a third copy; stored snapshots read back unchanged.
- **Schema.** One additive migration (`OutgoingCamt056`, EF CLI) widens the three type and definition constraints.

## Changed existing tests

- Outgoing tests that used `camt.056` as an unsupported message type (`AggregateOwnershipTests`, `PaymentPreparationTests`, `PaymentSubmissionTests`) use `pacs.003`; the incoming tests that use `camt.056` as an unsupported incoming type are unchanged, because incoming recalls are not built.
- `OutgoingBindingTests` expects the sixth route; the process-kill theory gains camt.056 rows. No assertion about existing behavior was weakened.
- `IpsReplyInterpreter`'s unresolved description no longer names pacs.008 for every type.

## New tests

- **Validation (unit):** every rule, normalization, the date boundary and the precision rule.
- **Processing (real SQL, Java-signed fixtures):** the v1 wire profile and schema validity, optional content (remittance with `SCOR`, ultimate parties, address, caller creation time), a reply that names the recall id instead of the recalled payment is not accepted, rejected and unknown replies, idempotency, reused ids (also against a pacs.009), id race, signature verification, crashes at four checkpoints, snapshot round trip.
- **Recovery:** flagged resend of the original bytes, backoff, window end, wrong-ids reply.
- **Host:** 200 with callback and 400, 504 with an immediate duplicate, flagged recovery of a lost reply over HTTP.
- **Process kills:** ready, marker, response and a resend response.
- **Status reports:** an unsolicited pacs.002 about a camt.056 settles it; one echoing our own ids is held.

## Independent reviews

**Standards.** No blocking findings. Fixed: the missing `response` process-kill case; blank address lines no longer count toward the seven-line limit; a comment stating the UTC date rule; the stale spec line about a per-message profile. Recorded: the two party validators repeat name, type and identifier rules (small, in one file); `ClearingSystem` and `LocalInstrument` constants are still declared in each message builder.

**Spec.** No blocking divergence from the source and no behavior change for other types. Recorded: the source skips the enabled-currency check when no currencies are configured and this port always enforces it, as the rebuild does for pacs.008; recall-specific tests for the attempt cap, abandoned marker and owner contention are left to the shared pacs.009 and pacs.004 tests; a caller in Georgia (UTC+4) sending a recall just after local midnight can have a same-day settlement date refused as future (the source behaves the same).

## Verification

- `dotnet build` (Release): 0 warnings, 0 errors.
- Full test run: **1,188 tests pass** (394 unit/architecture/Contracts, 794 integration), against a baseline of 1,123; 65 tests were added. An earlier full run had one failure in the existing `IncomingReconciliationTests` window-clamp timing test (it passes alone and in the rerun).
- `dotnet format` and the IDE0005 check: clean.
- `dotnet ef migrations has-pending-model-changes`: no changes; the migration was generated with the EF CLI.

## Remaining

- **No real IPS verification.** How real IPS answers a recall (the reply ids), the Annex D reason-code list and any recall time limit are unverified; they come from the source and its mock.
- A recall is not linked to the payment it recalls or to the pacs.004 or camt.029 that answers it.
- Outgoing camt.029 (007b), incoming camt.056 and camt.029 (007c, 007d; they need a Contracts receive method) and camt.055 (with pain.001) remain.
- Carried: three test-only production APIs, load-sensitive tests, the naming debt of 005a.
