# Review 005b: outgoing pacs.004 (payment return)

Branch codex/outgoing-pacs004, stacked on codex/outgoing-pacs009 (fc82368, merge approval pending). On 2026-10-06 the owner approved the decisions (FOCR only; no lookup of the original payment) and the [specification](../specs/005b-outgoing-pacs004.md); commit and merge approval are pending.

## Delivered

- **Intake.** `Pacs004Request`, a validator that ports the source rules (ids, amount, enabled currency, value date, agents, indirect participant, parties with name and IBAN, the original reference, returned amount not above the original), `ValidatedPacs004`, `AcceptedPacs004` and `Pacs004Intake`. The reason code is FOCR only, as the owner decided. The return id is both the message id and the transaction id and must be unused; the unique indexes fence a race.
- **Reply correlation per type.** `IAcceptedPayment.ReplyCorrelation` replaces the four places that built the correlation from stored ids (`OutgoingPaymentProcessing`, `OutgoingResend`, `OutgoingDuplicateResend`, `IncomingStatusReportProcessing`). pacs.008 and pacs.009 return exactly what they did before; pacs.004 returns our message id with the original payment's transaction and end-to-end ids, as the source's mock IPS answers.
- **XML.** `pacs.004.001.13` (`PmtRtr`) in the IPS v1 profile, validated against the embedded XSD (byte-identical to the source's) and signed like the other types.
- **API.** `POST /api/ips/pacs004/send` with the same 200, 504 and 400 results as the other types.
- **Recovery.** The possible-duplicate resend from 005a, unchanged; there is no pre-send deadline.
- **Schema.** One additive migration (`OutgoingPacs004`, generated with the EF CLI) widens the three type constraints.
- **Amount precision (found in review).** The pacs.008 rule "at most 13 integer and 5 fractional digits" now lives in `PaymentChecksums` and also applies to pacs.009 and pacs.004 (both amounts). Before, `1.234567` would have been accepted and sent as `1.23457`, and `1e13` would have failed the schema after acceptance.

## Changed existing tests

- `ProcessingHarness`, `OutgoingHostFixture`: the IPS simulators echo the original payment's ids for a return.
- `OutgoingProcessProbe` registers the pacs.004 intake; the pacs.009 process-kill theory takes the message type and gains pacs.004 rows (renamed).
- `OutgoingBindingTests` expects the fifth route.
- `Pacs009ValidationTests` gains the precision case. No assertion about pacs.008 or pacs.009 was weakened.

## New tests

- **Validation (unit):** every rule, defaults, mixed currencies, the precision rule.
- **Processing (real SQL, Java-signed fixtures):** the v1 wire profile and schema validity, optional content (original group, indirect participant, party ids), a reply naming the return instead of the original payment is not accepted, rejected and unknown replies, idempotency, reused ids (also against a pacs.009), id race, signature verification, crashes at four checkpoints, snapshot round trip.
- **Recovery:** flagged resend of the original bytes, backoff, window end, wrong-ids reply.
- **Host:** 200 with callback and 400, 504 with an immediate duplicate, flagged recovery over HTTP.
- **Process kills:** ready, marker, response and a resend response.
- **Status reports:** an unsolicited pacs.002 about a pacs.004 settles it, and one echoing our own ids is held.

## Independent reviews

**Standards.** No blocking findings. Fixed: an original currency without an original amount is now a validation error (it would have labelled the returned amount as the original); the "not kept" comment; the status-report test helper keeps its literal ids for pacs.008 and pacs.009. Recorded, not changed:
- `Pacs004Message` repeats protocol constants and small helpers that `Pacs008Message` and `Pacs009Message` also hold; extract when a fourth builder arrives, together with the profile classes.
- `IAcceptedPayment` imports the pacs.008 namespace only for `IpsReplyCorrelation`.
- A party type without an identifier is ignored.

**Spec.** No blocking divergence from the source and no pacs.008 or pacs.009 behavior change. Fixed: amount precision (above), the missing 504 and duplicate host test. Recorded: attempt cap, abandoned marker, saved-reply replay and competing or stale owners are covered by the pacs.009 recovery tests on the shared code, not repeated for pacs.004; the indirect participant keeps the caller's casing in the XML, as the source does.

## Verification

- `dotnet build` (Release): 0 warnings, 0 errors.
- Full test run: **1,071 tests pass** (349 unit/architecture/Contracts, 722 integration), against a baseline of 999; 72 tests were added.
- `dotnet format` and the IDE0005 check: clean.
- `dotnet ef migrations has-pending-model-changes`: no changes; the migration was generated with the EF CLI.

## Remaining

- **No real IPS verification.** The reply correlation (original ids), the possible-duplicate behavior and the v1 profile come from the source and its mock only.
- No lookup of the original payment: a caller can return a payment that never existed or was already returned; IPS is the only check.
- Carried: three test-only production APIs, the intermittent `Pacs008ProcessingTests ("unsigned")` failure, the naming debt of 005a.
