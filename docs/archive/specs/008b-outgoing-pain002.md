# Review 008b: outgoing pain.002 (refusal of a payment initiation)

Status: specification on codex/payment-initiation, stacked on 008a (29a38ca) and the earlier unmerged slices 005a-005d and 007a-007c (merge approval pending for all of them). The owner approved the decisions below and this specification on 2026-10-07; implemented, review pending.

## Scope

Add the outgoing pain.002 end to end on the shared outgoing core: intake and validation, XML, signing, `POST /api/ips/pain002/send`, send, outcome, CBS callback and possible-duplicate recovery. A pain.002 refuses a payment initiation (pain.001) that came in through [008a](008a-incoming-pain001.md); to accept the initiation the core sends a pacs.008 whose EndToEndId is `PSP-` plus the initiation id (already possible). The new part is how IPS's answer is read: unlike every other outgoing message, the outcome of a pain.002 is decided from a response header, not from a pacs.002 body. Incoming camt.055 (008c) and linking a `PSP-` pacs.008 to a stored initiation stay separate.

## Source evidence

Pinned source d498de6c4638aa71cdb20189d13642b41abab5f1: `Contracts/Pain002/Pain002PaymentStatusReportDto.cs`, `Application/Validation/PaymentInitiationValidators.cs` (`Pain002PaymentStatusReportValidator`), `API/Services/Pain002XmlMessageBuilder.cs`, `API/Transactions/IsoTransactionSenders.cs` (`Pain002TransactionSender`, including `Interpret`), `MockIps/Pain002ResponseBuilder.cs`, `MockIps/Validators/PaymentInitiationValidator.cs`, `Tests/Ips/Schemas/pain.002.001.14.xsd`, `docs/IPS_Seq_pain002_KA.drawio`, `docs/IPS_Pain001_Camt055_Cancellation_KA.md`.

- Route `POST /api/ips/pain002/send`, 202 in the source. Message id = `Id`. The refusal quotes the refused pain.001's message id and payment information id.
- IPS answers with a pain.002 and says whether it took the refusal only in the `X-MONTRAN-IPS-ReqSts` header: `ACCP` means IPS took it and forwards it to the PISP, `RJCT/<code>` means it rejected it, anything else (or no header) means the outcome is unknown and the message is resent as a possible duplicate. The body is not read.
- The source's mock rejects a second reply to the same pain.001 with AG09 / error 1017 (already answered). A resend after an unknown outcome can therefore be refused for an answer IPS in fact already holds.
- Validation: ClientReference; `Id`, `OriginalMessageId`, `OriginalPaymentInformationId` required and 35 ASCII; reason code required, 1-4 uppercase letters or digits; additional information at most 105 characters; originator name at most 140.
- XML `pain.002.001.14` (`CstmrPmtStsRpt`): `GrpHdr` (`MsgId`, `CreDtTm` = the caller's or now, `DbtrAgt` = our BIC), `OrgnlGrpInfAndSts` (`OrgnlMsgId`, `OrgnlMsgNmId`, `GrpSts` RJCT, `StsRsnInf` with `Rsn/Cd` and optional `AddtlInf`), `OrgnlPmtInfAndSts` (`OrgnlPmtInfId`, `PmtInfSts` RJCT, `StsRsnInf` with `Orgtr` and optional name, `Rsn/Cd`, optional `AddtlInf`), in XSD order. No pre-send deadline in the source.

## Owner decisions (2026-10-07)

1. **IPS's answer is read from the header only, as in the source.** `X-MONTRAN-IPS-ReqSts`: ACCP is Accepted, `RJCT/<code>` is Rejected, anything else is Unresolved (and so resent). The response body is neither verified nor required; what a real IPS pain.002 reply looks like is unverified and recorded as a limit.
2. **An RJCT, including AG09 / 1017 (already answered) on a resend, is a final Rejected** with IPS's reason and a callback, like any other rejection. The core sees the code and decides.
3. **`OrgnlMsgNmId` is `pain.001.001.12`**, as the source sends (it flags the `.012` spelling for confirmation on the IPS test environment); one constant to change.
4. Carried from 007a and 007b: the HTTP result is 200 with the final status or 504 with the current status, duplicates return the current status at once, and the stale 202 declaration of `SendPain002Async` is corrected (success code and description only, `public-api.json` edited explicitly, recorded in the baseline README).

## Design

Application `Payments/Pain002/`: `Pain002Request`, `Pain002Validator`, `ValidatedPain002`, `AcceptedPain002` and `Pain002Intake`, following 007a.

- **Identifiers.** The message id is `Id`; a pain.002 has no separate transaction id, so the stored protocol transaction id is `Id` too (as for pacs.004). The id must be unused across payments; reuse is a validation error and the unique indexes fence a race. The status shows the initiation's payment information id.
- **`PaymentMessageTypes`** gains `Pain002`, `Pain002Definition` (`pain.002.001.14`) and joins the outgoing set; recovery is the shared possible-duplicate resend; there is no pre-send deadline.
- **Reading IPS's answer.** `IIpsReplyInterpreter` becomes a dispatcher on the correlation's message definition: a pain.002 goes to a new `Pain002ReplyInterpreter` (the header rule above, plus a successful HTTP status; the result carries the header text and any numeric error code), everything else to the existing pacs.002 interpreter, unchanged. The reply correlation of a pain.002 snapshot is its message id and the initiation id with the pain.002 definition; the pain.002 interpreter does not use it.
- **Infrastructure `Payments/Pain002/`:** embedded `pain.002.001.14` schema, `Pain002Message`, `Pain002Xml`, `Pain002Preparation`, a signer overload and `Pacs008Schema.ValidatePain002`.
- **Persistence:** the snapshot JSON reads by message type; the three type and definition check constraints are widened (every occurrence) by one additive migration generated with the EF CLI.
- **API:** `POST Pain002RestApiRoutes.Send` with `Pain002RequestMapping`; `OutgoingRuntime` routes the new request type; the status route already covers the kind.
- **Contracts metadata:** `SendPain002Async` success code 202 to 200 and its description in the wording of the other sends.
- **Tests:** no existing test uses `pain.002` as an unsupported type.

## Acceptance

Real SQL and independently signed IPS fixtures:

- Validation: every rule above, including a reason code of five characters, additional information of 106 characters, a missing original message or payment information id; normalization.
- Intake: duplicate client reference, reused id, race on the unique indexes.
- XML: schema validity, the exact element set and order, `RJCT` in both places, the originator element with and without a name, additional information only when given, the fixed message name, signature verified independently, byte-stable resume at each checkpoint.
- Outcomes through the real reply path: `ACCP` header accepted; `RJCT/1017` and `RJCT` alone rejected with the IPS reason and a callback; a missing, odd or non-success response unresolved and resent; a pacs.002-shaped body with an ACCP header is still decided by the header; the pacs.002 interpreter and its tests are unchanged.
- HTTP: 200, 504 and an immediate duplicate; flagged recovery of a lost reply with the same bytes.
- Recovery: backoff, window end and process kills at ready, marker and response (the engine is covered by the earlier slices).
- Existing outgoing and incoming behavior is unchanged: the full existing suite passes with only the mechanical updates listed in the review; the public API baseline differs only in the one edited route declaration.
- Build, full tests, format, EF model and migration chain, independent Standards and Spec reviews.

## Not in scope

Camt.055 (008c), linking a `PSP-` pacs.008 or this refusal to the stored initiation or checking that the initiation exists (the source does not), verifying the reply body or signature, real IPS interoperability (the header rule and the AG09 behavior rest on the source and its mock), production activation.

## Implementation notes

- The header-only reading is dispatched inside `IpsReplyInterpreter.Interpret(response, sent)`: a correlation whose definition is `pain.002.001.14` goes to `Pain002ReplyInterpreter`, every other message keeps the signed pacs.002 reading. This replaces the separate composite the design sketched, so every existing call site and test is unchanged.
- The test IPS simulators correlate a pain.002 reply on `OrgnlPmtInfId`, because that is the identifier the correlation stores for it.

