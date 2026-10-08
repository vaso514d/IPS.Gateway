# Specification 012c: delivering incoming recalls, cancellations and recall refusals to the core

Status: specification on codex/payment-initiation, stacked on 012b (1ca0c2e) and the earlier unmerged slices. The owner decided on 2026-10-07:
- new Contracts receive methods on `IClientPaymentReceiver` (a new minor Contracts version; the core team implements the endpoints);
- incoming camt.056 and camt.055 requests are delivered to the core;
- an incoming camt.029 refusal is matched to our outgoing recall, recorded on it, and the core is told.

On 2026-10-08 the owner resolved the open decisions below (existing DTOs reused, a camt.029 without `OrgnlGrpInf` held, the ISO 20022 camt.055.001.12 schema added), and the specification is approved with version `1.1.0-preview.1`. Implemented and reviewed ([review evidence](../reviews/012c-recall-delivery.md)); commit approval pending.

## Scope

Today incoming camt.056, camt.029 and camt.055 are acknowledged and archived and nothing else happens (007c, 008c; also the source d498de6c, whose `IClientPaymentReceiver` has no method for them). This slice:

1. **Verifies** each of the three types like the other incoming types: definition, schema, trusted IPS signature at receipt time (012b), a single transaction, and we are the receiver. Anything unverifiable is Held, as today for transfers. Acknowledgement is unchanged: still after the receipt commit, whatever the outcome.
2. **Delivers** a verified camt.056 (a creditor-side recall request about a pacs.008 we received) and a verified camt.055 (a PISP's cancellation request about a pain.001 we received) to the core. Delivery is durable and retried, with the same machinery and recovery as incoming transfers (005c): submit, ask the core for the status after an unanswered call, resubmit after a 404, retry on the 005c schedule until the reconciliation window ends, then Manual Review.
3. **Matches and delivers** a verified camt.029 refusal (RJCR):
   - **Match.** Its `OrgnlGrpInf/OrgnlMsgId` names one of our outgoing camt.056 recalls (`MessageId`, the camt.056 Assgnmt/Id), and its OrgnlEndToEndId and OrgnlTxId must agree with that recall's original payment.
   - **Record.** The refusal is recorded on our recall as a business-outcome event: the creditor bank refused, with the reason code, the camt.029 ids and when. Our recall's IPS state does not change; it stays Accepted by IPS, as it is final.
   - **Deliver.** The camt.029 is delivered to the core with our recall's `ClientReference`, using the same delivery machinery.
   - **No match.** A camt.029 that names no recall of ours, or whose identifiers disagree, is Held with its reason and not delivered.

Not in scope:
- **Answers.** Answering a camt.055 (a camt.029 CNCL/RJCR about a pain.001, Annex D 3.2.6). Our outgoing camt.029 stays the recall refusal of 007b. The core answers a camt.056 with the existing pacs.004 (accept) or camt.029 (refuse) sends.
- **Correlating answers.** Correlating an incoming pacs.004 positive answer with our recall. Incoming pacs.004 is already delivered to the core as a transfer (005d), and it names the pacs.008, not the camt.056.
- **Deadlines.** Recall reason-code or deadline enforcement.
- **pacs.028.** pacs.028 about a camt.056.
- **Other types.** pain.013/pain.014.

## Evidence

- **Annex D v1.02 message definitions:**
  - camt.056 §3.2.4 (pages 41-42; table 8.1.5): we are the receiver as OrgnlTxRef/CdtrAgt; the originator is DbtrAgt.
  - camt.029 §3.2.5 (pages 44-45; table 8.1.6): Conf/TxCxlSts RJCR. `OrgnlGrpInf` is [0..1], with OrgnlMsgId the camt.056 Assgnmt/Id and OrgnlMsgNmId camt.056.001.11. We are the receiver as DbtrAgt.
  - camt.055 §3.2.13 (pages 74-75; table 8.1.13).
  - IPS answers each with a technical pacs.002 only (page 68).
- **Ids.** Annex D says Assgnmt/Id and CxlId are unique per sender within 24 hours (pages 42, 45).
- **Recall responses.** IPS does not check that a recall response matches a real recall (Inception Report v2.01, pages 35-36), so matching is the participant's job.
- **Schemas.** `camt.056.001.11.xsd` and `camt.029.001.13.xsd` are already in the repository. camt.055.001.12 is not.

## Design

**Contracts (an additive, approved compatibility change):**
- **Version.** `IPS.MiidleWear.Contracts` goes from `1.0.0-preview.1` to `1.1.0-preview.1`.
- **New methods on `IClientPaymentReceiver`.** Each has a `RestEndpoint`, a route constant and a summary saying that a 2xx/ACCP answer only means the core took the request; the business answer is sent later through `IGatewayApi`.

  | Method | Route constant | Path | Payload |
  |---|---|---|---|
  | `ReceiveCamt056Async` | `Camt056RestApiRoutes.Receive` | `/api/ips/camt056/receive` | `Camt056RecallRequestDto` |
  | `ReceiveCamt055Async` | `Camt055RestApiRoutes.Receive` | `/api/ips/camt055/receive` | `Camt055CancellationRequestDto` |
  | `ReceiveCamt029Async` | `Camt029RestApiRoutes.Receive` | `/api/ips/camt029/receive` | `Camt029ResolutionOfInvestigationDto` |

  - **Answer.** Each method returns `Pacs008PaymentResultDto`, as the other receive methods do; the precedent is pain.001.
  - **Payloads.** These are the existing DTOs, reused as `ReceivePacs008Async` and the others reuse their send shapes. Their documentation gains the receive meaning of each field (for example `ClientReference` on a delivered camt.029 is our recall's reference).
- **Status kind.** `IpsMessageKind` gains `Camt055` and `Camt029` if they are missing. The existing status query (`GetPaymentStatusAsync` with `messageKind` and `reference`) covers the new kinds.
- **Baselines.**
  - `public-api.json` is edited by hand for exactly these additions, not regenerated.
  - New wire cases are written by hand for a delivered camt.056, camt.055 and camt.029.
  - The baseline README records the approved exception. The source manifest and existing wire cases are unchanged.

**Delivery (Infrastructure and Application):**
- **New transfer kinds.** Three new `IIncomingTransferProtocol` readers (camt.056, camt.055, camt.029) produce `IIncomingTransferContent`, so the incoming-transfer storage, ownership, retries, status query and Manual Review are reused unchanged.
  - **Keys.** The key is the message's Assgnmt/Id: at most 35 characters, so a longer one is held, as pain.001's key is.
  - **Configuration.** `IncomingCbsClient` gains the three submission paths, configured like the others (`Camt056SubmissionPath`, `Camt055SubmissionPath`, `Camt029SubmissionPath`).
- **camt.029 matching** runs at registration, in Application. It looks up our outgoing camt.056 by `MessageId`, which is already indexed, and checks the original E2E and TxId. On a match, the recall gets its business-outcome event and the delivery is registered, in one transaction. Without a match, the receipt is Held.
- **Domain.** `OutgoingPayment` gains an event for a creditor-bank refusal of a recall. It is recorded only on a camt.056 payment; a repeated refusal for the same recall is recorded once. It changes no state and sends no status callback, because the core is told by the camt.029 delivery.
- **Schema.** camt.055.001.12 is added to the schemas from the ISO 20022 catalogue (see open decision 3).
- **Migration.** None expected: the transfer table, the event table and the indexes already exist. If one turns out to be needed, it is generated with the EF CLI.

## Acceptance

- **Delivery of camt.056 and camt.055:**
  - A verified message is stored once, delivered to the core with its Idempotency-Key, and finished when the core accepts it.
  - An unanswered call leads to a status query; a 404 leads to a resubmit.
  - The 005c retry schedule and the window end in Manual Review apply.
  - A redelivery is one delivery; changed content under the same key is held.
  - Two instances deliver it once.
- **Unverifiable input.** Any of these leaves the message Held and delivers nothing: a wrong definition, a schema error, an untrusted or out-of-date signature, several transactions, a receiver other than us, or a key that is too long.
- **camt.029 matching:**
  - A refusal of our recall records one refusal event on that recall (with reason and ids) and delivers the camt.029 with our recall's `ClientReference`. The recall's IPS state and callbacks are unchanged.
  - A camt.029 without `OrgnlGrpInf`, naming no recall of ours, or with disagreeing identifiers is Held, with nothing recorded or delivered.
  - A crash between matching and commit leaves nothing half-done.
- **Unchanged behaviour.** Acknowledgement and every other incoming type are unchanged.
- **Contracts.**
  - The Contracts test passes against the hand-edited baseline, with only the listed additions.
  - The new wire cases round-trip.
  - The Contracts project still depends only on the base class library.
- **Simulators.** The Aspire simulators accept the three new core routes (core side only).
- **Verification.** Build, the full suite (`-m:1`), format and imports, the EF check, and independent Standards and Spec reviews.

## Owner decisions (2026-10-08; the first option of each was chosen)

1. **Payload types:** reuse the existing camt.056, camt.055 and camt.029 DTOs, as the existing receive methods reuse their send shapes (recommended). The alternative is new receive-only DTOs, which are clearer but enlarge the Contracts change.
2. **A camt.029 without `OrgnlGrpInf`** (optional in Annex D): hold it for manual handling (recommended; nothing is guessed). The alternative is to match it by original E2E and TxId against our recalls. Those ids are only stored inside JSON today, so that needs a new indexed column and a migration.
3. **The camt.055 schema:** add camt.055.001.12 from the ISO 20022 catalogue (recommended). If it can't be obtained, verify camt.055 by definition and signature only, and record the gap.
4. **Contracts version:** `1.1.0-preview.1` (recommended).

## Risks

- **Core team dependency.** The core team must implement three endpoints and the status query for the new kinds before production use.
- **Key collisions.** The transfer key does not include the sending bank. Two banks using the same Assgnmt/Id within the window would produce a conflict, which is held, not lost. The same limit already applies to transfers.
- **Not checked against a real IPS.** Real camt.029 traffic may or may not fill `OrgnlGrpInf`.

## Implementation notes (2026-10-08)

- **Schema source.** `camt.055.001.12.xsd` was downloaded from `https://www.iso20022.org/sites/default/files/documents/messages/camt/schemas/camt.055.001.12.xsd` (HTTP 200, `Last-Modified: Mon, 11 Mar 2024`; the catalogue pages themselves answered 403). It is one self-contained schema without imports, generated by the Standards Editor on 2024-02-29 like the existing `camt.056.001.11.xsd`. Only its line endings were changed to LF (SHA-256 as downloaded `c3237d50...71106f5`, as stored `6d4dcd1f...156a1`). It is embedded and compiled with the other schemas.
- **Migration (deviation from "none expected").** "Recorded once per recall" is a Domain rule, and loading an aggregate never replays its events, so `OutgoingPayment` keeps `RecallRefusedAtUtc`. The EF CLI generated `RecallRefusal`, one nullable `datetimeoffset` column on `Transactions`.
- **Repeated refusals.** A redelivered camt.029 finds its stored transfer and records nothing again. A second camt.029 with another Assgnmt/Id that refuses the same recall still matches. It is stored and delivered under its own key, and the recall ignores the repeated refusal. The refusal is recorded whatever the recall's IPS status is; that status is not checked.
- **Key length.** Assgnmt/Id is a `Max35Text` in all three schemas, so a longer key is held by the schema check ("Malformed or unsupported ... content"), not by a reason of its own.
- **camt.055 versions.** The header definition `camt.055.001.12` and the registry spelling `camt.055.001.012` are read. A receipt of type `camt.055.001.08`, which was archived until now, reaches the reader and is held as an unsupported definition.
- **camt.029 checks.** Both `Sts/Conf` and `TxCxlSts` must be RJCR. `OrgnlMsgNmId` is not checked: the message id must still name one of our camt.056 recalls.
- **Mapping choices.**
  - Parties are read from the `Pty` choice.
  - Several `Ustrd` lines are joined, as for the other incoming types.
  - The camt.055 reason comes from `TxInf/CxlRsnInf`, or from the instruction's `CxlRsnInf` when the transaction has none.
  - The camt.055 DTO has plain `DateTime` fields: instants are given in UTC, and `RequestedExecutionDate` is the date at midnight (a `DtTm` is reduced to its date, as for pain.001).
  - `Camt055PartyDto.PostalAddress` (a raw `JsonElement`) carries the camt.056 postal address shape.
  - `Assgnr`/`Assgne` BICFIs fill `AssignerBic`/`AssigneeBic`.
  - Elements without a DTO field (for example `CxlRsnInf/Orgtr` and `OrgnlUETR` of a camt.056) are not delivered.
  - As for the other transfer kinds, the posted JSON contains `null` members, because `JsonContent` defaults are used.
- **Two instances.** This is shown at the claim level with real SQL: a second owner cannot claim a transfer while the first is calling the core, so the core is called once. No two-host test was added: the only two-host incoming test (`IncomingWorkerSqlTests.Two_hosts_receive_duplicates_...`) covers pacs.008, and there is no two-host transfer pattern to reuse cheaply.
- **Existing tests changed** (expectations follow from verification replacing archiving; no assertion was weakened):
  - `IncomingCompositionTests.A_recall_or_cancellation_is_archived_...` became `An_unverifiable_recall_or_cancellation_is_held_without_a_payment_a_transfer_a_reply_or_any_remote_call`. The same eight types with unsigned stand-in XML are now Held, with a hold reason and no transfer. The harness also registers the three new readers.
  - In `IncomingTransferTests.Worker_acknowledges_recalls_and_cancellations_...`, the unsigned stand-ins are now Held instead of Processed. The acknowledgements, the redelivery count and the metrics are unchanged. `ApplyAsync` passes the new `IncomingRecallRefusals` dependency.
  - `PaymentMessageTypesTests.Only_a_pacs009_a_return_and_an_initiation_...` became `Transfers_recalls_refusals_and_cancellations_are_handed_to_the_core_as_transfers`. camt.056 is now true; camt.029, both camt.055 spellings and `.08` are true; camt.053 is false.
- **Simulators.** `/api/ips/camt056|camt055|camt029/receive` record a `CoreDeliveries` entry (message, body, Idempotency-Key, time), exposed by `/_sim/received`, and answer `{"status":"ACCP"}`.
- **Review fixes.**
  - **Recall in progress.** A matched refusal whose recall is under another owner's live claim (an uncertain recall being investigated) defers the receipt for the composition continuation delay, as a status report does. `ITransactionWorkRepository.StageUnclaimedObservation` refuses to fence a claimed recall, so the owner's commit is never discarded.
  - **No accepted content.** A refusal naming a recall without accepted content is held ("Our recall has no accepted content to compare the refusal with."), as a status report is.
  - **Shared date reading.** The requested execution date of pain.001 and camt.055 is read by one shared `RequestedExecutionDate`.
  - **Dates with a time zone.** A camt.056 or camt.029 original date that carries a time zone is held as malformed, as for incoming pacs.009 and pacs.004 (`DateOnly.Parse`). That is not the pain.001 rule, which drops the date.
  - **Not done.** The matching does not compare the refusal's creditor agent with the creditor agent of the payment we recalled. The specification does not require it; this is recorded in the review.
