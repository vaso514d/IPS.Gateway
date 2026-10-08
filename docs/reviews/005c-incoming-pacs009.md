# Review 005c: incoming pacs.009

Branch codex/incoming-pacs009, stacked on codex/outgoing-pacs004 (e65c3a5) and codex/outgoing-pacs009 (fc82368); neither merge is approved. On 2026-10-06 the owner approved the three decisions (MessageAck only, a separate lightweight aggregate, schema plus signature plus mandatory-field validation) and the [specification](../specs/005c-incoming-pacs009.md); commit and merge approval are pending.

## Delivered

- **Receive and verify.** `IncomingReceiptPreparation` routes a pacs.009 receipt to `IncomingPacs009Processing`. `IncomingPacs009Protocol` checks the envelope and definition, the trusted IPS signature, that there is exactly one transfer, the schema, and that both agents carry a BICFI, then maps the source's field rules (ids, `DbtrAgt` else `Dbtr`, priority, value date, code or proprietary category purpose, flat purpose, joined remittance, IBAN else `Othr/Id`). Anything else holds the receipt with a reason and delivers nothing. The creditor agent must be our participant.
- **Store once.** `IncomingFiTransfer` (Domain) is keyed by participant BIC and exact EndToEndId (binary collation, byte-length unique index). A redelivery with the same content completes at once; different content under the same key holds the receipt as a conflict. The receipt is complete once the transfer is stored.
- **Hand over and settle.** `IncomingTransferProcessing` claims a due transfer, commits the attempt before the call, then submits (first call) or asks the core (after an unanswered call). ACCP or RJCT that echoes the transfer's identifiers is final; anything else is Unknown and retried on the incoming reconciliation backoff (30 s, 1 min, 5 min, then 15 min) up to the 24-hour window, which ends in manual review. A 404 to a status question means the core never saw the transfer, so the same request is sent again under the same key. The EndToEndId is the idempotency key, so a crashed owner is simply replaced and asks the core.
- **Acknowledge.** `IncomingReceiveWorker` sends `MessageAck` for a pacs.009 after the receipt commit, as for status reports; a failed ack never stops polling and a redelivery is acknowledged again. IPS gets no pacs.002.
- **Wiring.** The follow-up worker also discovers due transfers, alternating with payments so neither starves; `IncomingCbsClient` submits to the new `Pacs009SubmissionPath` and asks `messageKind=Pacs009`; configuration and `docs/configuration.md` list the new path.
- **Schema.** One additive migration (`IncomingTransfers`, EF CLI) adds the table and widens the aggregate-kind and event-kind constraints.

## Changed existing tests

- `IncomingCompositionTests` registers the pacs.009 protocol like the status-report protocol.
- `IncomingCompositionTests` and `IncomingWorkerSqlTests` used `pacs.009` as an unsupported type; they now use `camt.056`.
- No assertion about existing behavior was weakened.

## New tests

- **Domain (unit):** the transfer's state machine, attempt counting and guards.
- **Integration (real SQL, Java-signed fixtures):**
  - reader: valid message and the source's alternative wire forms (`Othr` account, `NORM`, code category purpose, proprietary purpose, no member id), and each unverifiable case (definition, signature, schema, two transfers, not our creditor, missing debtor or creditor BICFI, malformed);
  - identity: redelivery, conflicting content, a missing value date staying absent across days;
  - delivery: one submission with the key, ACCP and RJCT final, a reply naming other identifiers refused;
  - recovery: backoff schedule, later answer, 404 resubmission with the same request, window end, competing owner, stale owner, crash before the outcome commits;
  - worker: pull, store, acknowledge (also when the ack fails) and hand to the core over HTTP.

## Independent reviews

**Standards.** No blocking findings. Fixed: the follow-up dispatch is two plain steps that alternate between payments and transfers instead of one nested expression that favoured payments; the status kinds are named once (`IpsMessageKind`); the registered transfer id is returned; missing tests for alternative wire forms and a stale owner. Recorded, not changed: the sibling reconciliation re-checks ownership after its marker and before dispatch, which here is covered by the idempotency key and the row-version fence; the first delay is the shared options' 30 s, not the source's 10 s; transfer JSON uses `IncomingPaymentJson`, named for payments.

**Spec.** No blocking divergence. Fixed: a value date IPS did not send is no longer filled with the arrival day (a redelivery after midnight would have looked like a conflict); it stays absent and the core receives it empty. Recorded: a pacs.009 that is later held is still acknowledged, as for status reports; a core reply without identifiers is accepted on ACCP or RJCT, as for pacs.008; a debtor agent with a member id but no BICFI is held (stricter than the source, per the decision).

## Verification

- `dotnet build` (Release): 0 warnings, 0 errors.
- Full test run: **1,103 tests pass** (357 unit/architecture/Contracts, 746 integration), against a baseline of 1,071; 32 tests were added. An earlier full run had one failure in the existing `Pacs009ProcessingTests` signature test (a load-sensitive Java start; it passes alone and in the rerun).
- `dotnet format` and the IDE0005 check: clean.
- `dotnet ef migrations has-pending-model-changes`: no changes; the migration was generated with the EF CLI.

## Remaining

- **No real IPS verification.** That IPS wants only a `MessageAck` for a pacs.009, and the core's key and status route, come from the source only.
- The status route for a pacs.009 (`messageKind=Pacs009`) and the receive route must be implemented by the core system.
- Not covered: a stuck-Received transfer rule (a transfer is always due from registration, so it needs none) and non-positive sequences for pacs.009 (handled before the type check, as for the other types).
- Carried: three test-only production APIs, the intermittent `Pacs008ProcessingTests ("unsigned")` failure, the load-sensitive `OutgoingStatusDeliveryTests` 2xx case, the naming debt of 005a.
