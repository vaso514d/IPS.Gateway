# Review 012c: delivering incoming recalls, cancellations and recall refusals to the core

Branch codex/payment-initiation, stacked on 012b (1ca0c2e) and the unmerged slices before it; no merge is approved.
- **Contracts, 2026-10-07.** The owner decided to add Contracts receive methods and deliver incoming camt.056 and camt.055 to the core. An incoming camt.029 refusal is matched to our recall, recorded on it, and delivered to the core.
- **Specification, 2026-10-08.** The owner approved the [specification](../specs/012c-recall-delivery.md), choosing to:
  - reuse the existing DTOs;
  - hold a camt.029 without `OrgnlGrpInf`;
  - add the ISO 20022 camt.055.001.12 schema;
  - version the Contracts as `1.1.0-preview.1`.
- **Commit.** Commit approval is pending.

## Delivered

- **Verification.** `IncomingCamt056Protocol`, `IncomingCamt055Protocol` and `IncomingCamt029Protocol` read the three types in the same order as the other incoming readers:
  1. safe reader;
  2. envelope;
  3. `MsgDefIdr`;
  4. trusted IPS signature at receipt time (012b);
  5. single transaction;
  6. schema;
  7. receiver.

  Anything unverifiable is Held. `camt.055.001.12.xsd` comes from iso20022.org (source and hashes in the spec).
- **Delivery.** The three types are incoming transfer kinds (key = Assgnmt/Id), so the 005c machinery is reused unchanged: Idempotency-Key submission, status query, 404 resubmit, retry schedule, window, Manual Review and claims. `IncomingCbsClient` posts the existing DTOs to three configurable submission paths.
- **camt.029 matching.** `IncomingRecallRefusals` matches a refusal on several conditions:
  - its `OrgnlGrpInf/OrgnlMsgId` names one of our camt.056 recalls;
  - its original E2E and TxId agree with that recall's;
  - its Conf and TxCxlSts are RJCR.

  On a match, `OutgoingPayment.RecordRecallRefusal` records one `RecallRefused` event and `RecallRefusedAtUtc`, without changing state or sending a callback. The delivery carries our recall's `ClientReference`. Both commit in one save. Anything unmatched is Held.
- **Contracts `1.1.0-preview.1`.**
  - **New members:** `ReceiveCamt056Async`, `ReceiveCamt055Async` and `ReceiveCamt029Async` on `IClientPaymentReceiver`, `Receive` route constants, and `IpsMessageKind.Camt055 = 8`.
  - **Baselines:** `public-api.json` was hand-edited, three wire cases were hand-written, and the README exception records that implementers of `IClientPaymentReceiver` must add the methods.
- **Migration.** `RecallRefusal` adds one nullable column. It was generated with the EF CLI. It was needed because "recorded once" must survive reloads, and aggregates do not replay events.
- **Also updated.**
  - **Simulators:** the three core routes, recorded as `CoreDeliveries`.
  - **Configuration:** the three submission paths in `appsettings.json` and `docs/configuration.md`.
  - **Architecture:** `docs/architecture.md`; 007c and 008c are marked superseded.

## Tests

- **New: `IncomingRecallTests`**, real SQL with Java-signed messages:
  - **Content and definitions.** Stored content for all three types; the registry spelling of camt.055 is read, and `.08` is held.
  - **Hold cases, each type.** Wrong definition, schema error, untrusted signature, several transactions, wrong receiver, too-long key, malformed XML, and all four certificate-validity bounds.
  - **Delivery.** Redelivery and conflict; one core call and a final answer; status questions after an unanswered call, and a resubmit after a 404; window end to Manual Review; two owners deliver once.
  - **Refusals.** A matched refusal records one event and is delivered with our client reference, with the recall's state and callbacks unchanged. A repeated refusal is recorded once. Six unmatched cases are held with nothing recorded or delivered. A crash before commit leaves nothing.
  - **Recall in progress (review fix).** A refusal of a recall that its owner is working on is deferred, and is recorded after the claim ends.
  - **Workers.** Worker end to end for each type.
- **Proven to fail without the feature:**
  - with the identifier check skipped, the disagreeing-id cases failed;
  - without the once-per-recall guard, the repeated-refusal test failed;
  - without the recording and the camt.056 routing, the refusal and camt.056 worker tests failed.
- **Changed existing tests, none weakened:**
  - **`IncomingCompositionTests`:** the archive test now expects the eight unsigned stand-in types to be Held with no transfer.
  - **`IncomingTransferTests`:** in the acknowledgement test the unsigned stand-ins are Held instead of Processed, with acknowledgements, redelivery count and metrics unchanged. The registration helper passes the new dependencies.
  - **`PaymentMessageTypesTests`:** camt.056, camt.029 and camt.055 are now transfer types, and camt.053 is still not.

## Verification

Build 0 warnings; format and imports (IDE0005) clean; no pending EF model changes; unit 487/487 (the Contracts compatibility test included). Before the review fixes, the implementation's targeted integration run (Inbound, Payments.Camt0*, status reports, incoming transport) passed 518/518. The owner asked to commit while the post-fix targeted run (recall, transfer and composition classes) and the full suite were still pending; their results are recorded in the next slice's review.

## Independent reviews

**Standards.** No blocking findings.

Fixed:
- **Data failure.** The refusal lookup threw on a recall without accepted content; it is now held, as a status report is.
- **Discarded owner work.** A refusal fenced a recall under another owner's live claim, which would have discarded that owner's work. It now defers, as status reports do.
- **Duplicate code.** The requested-execution-date reading was duplicated in pain.001 and camt.055; it is now one helper.
- **README.** The README now gives the wire-case count (18) and a line for implementers of `IClientPaymentReceiver`.

Recorded:
- **Crash test.** It fails the save before any SQL is sent, so it proves the single save rather than mid-batch rollback; the single save makes both equivalent.
- **Concurrency.** Concurrent duplicate refusals are covered by the unique transfer key and the recall's row version, but are not tested concurrently.
- **Creditor agent.** Matching does not compare the creditor agent (hardening beyond the specification).
- **Dates with a time zone.** An original date with a time zone is held as malformed, as for pacs.009 and pacs.004.
- **Domain literal.** The Domain repeats the `"camt.056"` literal, because it cannot reference Application.
- **Unexercised simulator routes.** The simulator routes are not exercised by an Aspire test, because the stack has no IPS-side source of these messages yet.

**Spec.** No blocking findings; every Scope, Design and Acceptance item has code behind it.

Fixed:
- **Data failure.** The same hold on missing recall content.
- **Repeated refusals.** The contract documentation now says a recall can be refused more than once, each refusal delivered under its own id.
- **Ledger wording.** The ledger said a refusal "closes" the recall; its IPS state does not change.

Recorded as accepted:
- **Spec deviations.** The listed deviations (migration, a second different refusal delivered, the schema holding a too-long key, the camt.055 `DateTime` choices, two owners proved at the claim level).
- **Test gaps.**
  - Status queries for Camt056 and Camt029 go through the recording fake only.
  - No conflict test for changed camt.029 content.
  - No Domain unit test for the "only a recall" guard.

## Limits

- **Core endpoints required.** The core team must implement the three endpoints and the status query for the new kinds before production.
- **Not checked against a real IPS.** Real traffic may or may not fill `OrgnlGrpInf` in a camt.029; without it the refusal is held.
- **Key collisions.** The transfer key excludes the sending bank, so two banks reusing an Assgnmt/Id within the window produce a held conflict.
- **Answering a camt.055 is not built.** That needs a camt.029 CNCL/RJCR about a pain.001.
