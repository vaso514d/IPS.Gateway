# Review 007b: outgoing camt.029 (negative answer to a recall)

Branch codex/recalls, stacked on 007a (ababd13) and the unmerged slices 005a-005d; no merge is approved. The owner approved the decisions that govern it on 2026-10-06 (the 200 or 504 convention, no lookup, the source reason-code pattern) with the 007a slicing, then the [specification](../specs/007b-outgoing-camt029.md). Commit and merge approval are pending.

## Delivered

- **Intake and validation.** `Camt029Request`, a validator that ports every source rule (ids, reason code `^[A-Z0-9]{1,4}$`, additional information up to 105 characters, the quoted currency enabled, amount, settlement date not in the future, parties, agents, remittance and ultimate parties, and the creditor agent being our participant), `ValidatedCamt029`, `AcceptedCamt029` and `Camt029Intake`. The caller chooses the message id (`Id`) and the cancellation status id; both must be unused and the unique indexes fence a race.
- **XML.** `camt.029.001.13` (`RsltnOfInvstgtn`) in the IPS v1 profile: assignment from our participant to IPS, `Sts/Conf` RJCR, one `TxInfAndSts` with the status id, the original group (name id fixed to `camt.056.001.11`), original ids, `TxCxlSts` RJCR, the reason with our BIC as originator and an optional additional-information line, and the original transaction reference with the recalled amount first. Validated against the embedded schema and signed like the other types. The answer is always a refusal; to accept a recall the caller sends a pacs.004.
- **Reply correlation.** Our message id with the recalled payment's transaction and end-to-end ids and `camt.029.001.13`; the status shows the original end-to-end id. "Accepted" is IPS's technical verdict only.
- **API and recovery.** `POST /api/ips/camt029/send` returns 200 with the final status or 504 with the current status; duplicates return the current status at once; unknown outcomes are resent as possible duplicates exactly as for the other types.
- **Shared recall parts (refactor, behavior unchanged).** The party, address, agent and remittance inputs, their normalization (`RecalledTransaction.From`), the validators (`RecallOriginalValidator`, which takes the sending side) and the XML pieces (`RecallXml`) moved out of the camt.056 files into `Payments/Recalls`, and one `RecallRequestMapping` maps both DTOs. The camt.056 tests pass unchanged apart from namespaces.
- **Schema.** One additive migration (`OutgoingCamt029`, EF CLI) widens the three type and definition constraints (every occurrence).

## Changed existing tests

- camt.056 tests gain a `using` for the new namespace; `OutgoingBindingTests` expects the seventh route; the process-kill theory gains camt.029 rows. No assertion was weakened.

## New tests

- **Validation (unit):** every rule, normalization, the creditor-agent rule against the debtor-agent freedom.
- **Processing (real SQL, Java-signed fixtures):** the v1 wire profile and schema validity, optional content, a reply that names the cancellation status id instead of the recalled payment is not accepted, rejected and unknown replies, idempotency, reused ids, id race, signature verification, crashes at four checkpoints, snapshot round trip.
- **Recovery:** flagged resend of the original bytes, backoff, window end, wrong-ids reply.
- **Host:** 200 with callback and 400, 504 with an immediate duplicate, flagged recovery of a lost reply over HTTP.
- **Process kills:** ready, marker, response and a resend response.
- **Status reports:** an unsolicited pacs.002 about a camt.029 settles it; one echoing our own ids is held.

## Independent reviews

**Standards.** No blocking findings; the camt.056 XML and rules are unchanged by the refactor (element order, optional omission and formats compared before and after). Fixed: the camt.029 quoted currency and amount stay nested under `originalTransaction` (`Camt029OriginalInput` derives from the shared input and `RecallOriginalValidator<T>` carries it), so a 400 names the fields the caller sent; a comment on the fixed camt.056 name id; a stale mapping name in the spec; a test for blank additional information. Recorded: the sending side is a mode of the shared validator, which is acceptable at two consumers; the shared XML helper takes nullable amount and additional-information arguments.

**Spec.** No blocking divergence from the source. Verified element for element against the source builder and schema, the migration widens every occurrence of the type lists, and the camt.056 refactor preserved validation messages, normalization and the stored JSON shape. Recorded: the amount-precision rule is new for camt.029 (the shared 007a rule, which the schema would enforce anyway); the reply correlation rests on the source's mock IPS.

## Verification

- `dotnet build` (Release): 0 warnings, 0 errors.
- Full test run: **1,242 tests pass** (421 unit/architecture/Contracts, 821 integration), against a baseline of 1,188; 54 tests were added.
- `dotnet format` and the IDE0005 check: clean.
- `dotnet ef migrations has-pending-model-changes`: no changes; the migration was generated with the EF CLI.

## Remaining

- **No real IPS verification.** How real IPS answers a camt.029 (the reply ids) comes from the source and its mock only.
- A refusal is not linked to the recall it refuses, and nothing checks that the recall was received; the source has neither.
- Incoming camt.056 and camt.029 (007c, 007d; they need a Contracts receive method and an owner decision) and camt.055 (with pain.001) remain.
- Carried: three test-only production APIs, load-sensitive tests, the naming debt of 005a.
