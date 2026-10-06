# Review 007b: outgoing camt.029 (negative answer to a recall)

Status: approved by the owner on 2026-10-06 and implemented on codex/recalls, stacked on 007a (ababd13) and the earlier unmerged slices 005a-005d (merge approval pending for all of them). It carries the decisions the owner approved for 007a on 2026-10-06 and adds none.

## Scope

Add the outgoing camt.029 end to end on the shared outgoing core, exactly as [007a](007a-outgoing-camt056.md) did for camt.056: intake and validation, XML, signing, `POST /api/ips/camt029/send`, send, outcome, CBS callback and possible-duplicate recovery. A camt.029 refuses a recall this bank received as the creditor bank (the answer is always RJCR); to accept a recall the caller sends a pacs.004 (005b). Incoming camt.056 and camt.029 (007c, 007d) and camt.055 stay separate. No Contracts change: the DTO and route exist, and the success-code metadata was corrected in 007a.

## Source evidence

Pinned source d498de6c4638aa71cdb20189d13642b41abab5f1: `Contracts/Camt029/Camt029ResolutionOfInvestigationDto.cs`, `Application/Validation/RecallRequestValidators.cs` (`Camt029ResolutionOfInvestigationValidator`), `API/Services/RecallXmlMessageBuilder.cs` (`BuildCamt029`), `API/Transactions/IsoTransactionSenders.cs` (`Camt029TransactionSender`), `MockIps/Validators/RecallRequestValidator.cs`, `Tests/Ips/Schemas/camt.029.001.13.xsd`.

- Route `POST /api/ips/camt029/send`, 202 in the source. Message id = `Id`; the answer is `TxInfAndSts/CxlStsId = CancellationStatusId`. IPS answers synchronously with a technical pacs.002 (ACCP accepted and forwarded, RJCT rejected); that verdict is the final status. Nothing links the answer to the recall it refuses, and the source's mock does not check that a recall exists.
- Validation: ClientReference; `Id`, `CancellationStatusId`, `OriginalMessageId`, `OriginalEndToEndId`, `OriginalTransactionId` required and 35 ASCII; reason code required, 1-4 uppercase letters or digits (`Rsn/Cd`); additional information at most 105 characters; `OriginalTransaction` required with currency (required, 3 letters, enabled), amount greater than zero, settlement date (required, not in the future, §3.2.5.f), and the same parties, agents, remittance and ultimate parties as a camt.056; **the creditor agent must be our participant** (§3.2.5.g).
- XML `camt.029.001.13` (`RsltnOfInvstgtn`): `Assgnmt` (as for camt.056), `Sts/Conf` = `RJCR`, `CxlDtls/TxInfAndSts` with `CxlStsId`, `OrgnlGrpInf` (original message id; name id fixed to `camt.056.001.11`), original end-to-end id and transaction id, `TxCxlSts` = `RJCR`, `CxlStsRsnInf` (originator `AnyBIC` = our BIC, `Rsn/Cd`, optional `AddtlInf`) and `OrgnlTxRef` as for camt.056 plus `IntrBkSttlmAmt` first, in XSD order.
- No pre-send deadline; unknown outcomes are resent as possible duplicates. A pacs.002 reply echoes the original end-to-end id and transaction id and our message id (from the source's mock IPS).

## Design

The transaction reference is the same structure as camt.056's, so the shared parts move to a neutral place now that there are two consumers.

- **Application `Payments/Recalls/`** (moved from `Camt056/`, namespace updated, behavior unchanged): the party, ultimate party, address, agent and remittance input records and their normalized forms, the party/agent/address/remittance validators and the normalization of the original transaction block. camt.056 keeps its own request, validator, `ValidatedCamt056`, `AcceptedCamt056` and intake; the shared pieces are reused by both.
- **Application `Payments/Camt029/`:** `Camt029Request`, `Camt029Validator`, `ValidatedCamt029`, `AcceptedCamt029` and `Camt029Intake`.
  - Identifiers: message id = `Id`, transaction id = `CancellationStatusId`, both caller-chosen and unique across payments; the status shows the original end-to-end id.
  - Reply correlation: our message id, the original transaction id, the original end-to-end id and `camt.029.001.13`.
  - The shared amount-precision rule applies to the quoted amount; today's date is passed to the validator as in 007a.
- **`PaymentMessageTypes`** gains `Camt029`, `Camt029Definition` (`camt.029.001.13`) and joins the outgoing set; recovery is the shared possible-duplicate resend.
- **Infrastructure `Payments/Camt029/`:** embedded `camt.029.001.13` schema, `Camt029Message`, `Camt029Xml`, `Camt029Preparation`, a signer overload and `Pacs008Schema.ValidateCamt029`. The `OrgnlTxRef` element builder moves to a shared `RecallXml` helper in `Payments/Recalls/` and serves both messages (the amount element is written only for camt.029).
- **Persistence:** the snapshot JSON reads by message type; the three type and definition check constraints are widened (every occurrence) by one additive migration generated with the EF CLI.
- **API:** `POST Camt029RestApiRoutes.Send` with the shared `RecallRequestMapping`, 200 or 504 as for the other types; `OutgoingRuntime` routes the new request type.
- **Tests:** the camt.056 tests keep passing unchanged apart from namespaces; `camt.029` is not used as an unsupported-type stand-in anywhere.

## Acceptance

Real SQL and independently signed IPS fixtures:

- Validation: every rule above, including a creditor agent that is not our participant (and a debtor agent that is not, which is allowed), a settlement date in the future, a missing amount or currency, a bad IBAN, additional information of 106 characters, a reason code of five characters; normalization.
- Intake: duplicate client reference, reused id or cancellation status id, race on the unique indexes.
- XML: schema validity, exact element set and order, `RJCR` in both places, name id fixed to `camt.056.001.11`, additional information only when given, the amount element present, optional parts omitted, signature verified independently, byte-stable resume at each checkpoint.
- Outcomes: accepted, rejected, unknown; a reply naming the original transaction and end-to-end ids is accepted and one naming the cancellation status id is not; callback created; 200, 504 and an immediate duplicate over HTTP.
- Recovery: flagged resend of the exact original bytes, backoff, window end and process kills at ready, marker and response (the engine is covered by the earlier slices).
- An unsolicited pacs.002 about a camt.029 settles it.
- camt.056, pacs.008, pacs.009, pacs.004 and incoming behavior is unchanged: the full existing suite passes with only the mechanical updates listed in the review.
- Build, full tests, format, EF model and migration chain, independent Standards and Spec reviews.

## Not in scope

Checking that the refused recall exists or was received (the source does not), a positive answer by camt.029 (a pacs.004 is the positive answer), incoming recalls and their delivery to the core, camt.055, real IPS interoperability (the reply correlation rests on the source and its mock), production activation.
