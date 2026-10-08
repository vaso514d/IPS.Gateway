# Review 005b: outgoing pacs.004 (payment return)

Status: approved by the owner on 2026-10-06 and implemented on codex/outgoing-pacs004, stacked on codex/outgoing-pacs009 (commit fc82368, merge approval pending). The two decisions below were approved the same day.

## Scope

Add the outgoing pacs.004 end to end on the shared outgoing core from [005a](005a-outgoing-pacs009.md): intake and validation, XML, signing, `POST /api/ips/pacs004/send`, send, outcome, CBS callback and possible-duplicate recovery. No Contracts change (the DTO and route exist). Incoming pacs.009/pacs.004 (005c, 005d), camt.056 recalls and camt.029 answers stay separate; a pacs.004 sent as the positive answer to a recall is the same message and needs nothing extra here.

## Source evidence

Pinned source d498de6c4638aa71cdb20189d13642b41abab5f1 (IPS.MiidleWear.*): `Contracts/Pacs004/Pacs004PaymentReturnRequestDto.cs`, `Application/Validation/Pacs004PaymentReturnRequestValidator.cs`, `Domain/Payments/Pacs004PaymentReturnInstruction.cs` and `Pacs004PaymentReturnInstructionMapper.cs`, `API/Services/Pacs004XmlMessageBuilder.cs`, `API/Transactions/IsoTransactionSenders.cs` (`Pacs004TransactionSender`), `TransactionRecovery.cs`, `Tests/Ips/Schemas/pacs.004.001.13.xsd`; `MockIps/Validators/Pacs004RequestValidator.cs` and `Pacs002ResponseBuilder.cs` for what a reply echoes.

- Route `POST /api/ips/pacs004/send`, 202 in the source. Message id = return id = `Id`; the return is `TxInf/RtrId = Id`.
- Validation: ClientReference and Id (35 ASCII), amount > 0, enabled currency, value date, instructed agent BIC (required), instructing agent (if given, must be our BIC; defaults to it), sender indirect participant (if given, must be configured), return reason code (4 uppercase alphanumeric, default FOCR), TTC; the original block: transaction id, end-to-end id and value date required, optional instruction id, UETR, original message id and name id, original amount and currency; debtor and creditor of the original payment (name up to 140, IBAN with checksum, optional id with type); original amount may not be smaller than the returned amount in the same currency. Originator name/address and additional info are validated but never sent; debtor and creditor SWIFT are for the caller only.
- XML `pacs.004.001.13` (`PmtRtr`), IPS v1 profile: group header with MsgId, CreDtTm, NbOfTxs, `TtlRtrdIntrBkSttlmAmt`, `IntrBkSttlmDt` (value date), `SttlmInf` (CLRG, IPS) and `InstgAgt` (no `InstdAgt`); transaction with `RtrId`, optional `OrgnlGrpInf` (when both original message id and name id are given), `OrgnlEndToEndId`, `OrgnlTxId`, `OrgnlIntrBkSttlmAmt` (original amount, else the returned amount), `RtrdIntrBkSttlmAmt`, `ChrgBr` SLEV, `RtrRsnInf` (originator `AnyBIC` = our BIC, reason code) and `OrgnlTxRef` (original value date, `PmtTpInf` SvcLvl and LclInstrm, `Dbtr`/`Cdtr` as parties with name and optional id, IBAN accounts, `DbtrAgt` = instructed agent, `CdtrAgt` = our BIC plus the indirect participant as `ClrSysMmbId`). Never sent: `OrgnlInstrId`, UETR, `OrgnlCreDtTm`, `OrgnlIntrBkSttlmDt`, the transaction-level `IntrBkSttlmDt`, `RtrRsnInf/AddtlInf`, `InstdAgt`.
- No pre-send deadline. A pacs.004 reply is a pacs.002; the mock IPS echoes the original payment's `OrgnlTxId` and `OrgnlEndToEndId` and our message id as `OrgnlMsgId`. Recovery is the same possible-duplicate resend as pacs.009.

## Owner decisions (2026-10-06)

1. **Reason code: FOCR only** (Annex D §3.2.3.j). The default is FOCR and any other code is a validation error at intake. This is stricter than the source's validator, which only its mock IPS enforced.
2. **No lookup of the original payment**, as in the source: the caller's reference is trusted for content and only checked for format and for the amount not exceeding the stated original amount.

Carried from 005a: the HTTP result is 200 with the final status or 504 with the current status after the wait, duplicates return the current status at once, and unknown outcomes are resent as possible duplicates.

## Design

Application `Payments/Pacs004/`: `Pacs004Request` (record with the original reference and the two parties as nested input records), `Pacs004Validator` (the rules above, parties and IBAN rules shared with pacs.009 where identical), `ValidatedPacs004`, `AcceptedPacs004`, `Pacs004ProtocolProfile` (IPS BIC, service level; the same shape as pacs.009's) and `Pacs004Intake`.

- Identifiers: message id = transaction id = `Id`, both unique across payments exactly as for pacs.009 (the caller picks them; reuse is a validation error; the unique indexes fence a race).
- `IAcceptedPayment` gains the reply correlation: each accepted snapshot says which message id, transaction id and end-to-end id its pacs.002 reply must reference. For pacs.008 and pacs.009 that is what is sent today. For pacs.004 it is our message id, the original transaction id and the original end-to-end id, as in the mock; this replaces the three places that build the correlation from the stored ids, so `OutgoingPaymentProcessing`, `OutgoingResend`, `OutgoingDuplicateResend` and `IncomingStatusReportProcessing` stay type-agnostic.
- `PaymentMessageTypes` gains `Pacs004`, `Pacs004Definition` (`pacs.004.001.13`) and joins the outgoing set. `HasInvestigation` stays pacs.008-only, so recovery is the shared possible-duplicate resend.
- Infrastructure `Payments/Pacs004/`: embedded `pacs.004.001.13` schema, `Pacs004Message` (the XML above, XSD order), `Pacs004Xml`, `Pacs004Preparation : IOutgoingMessageProtocol`, a signer overload and `Pacs008Schema.ValidatePacs004`. Registered with the other protocols.
- Persistence: the accepted snapshot JSON reads by message type; the type check constraints and the message-definition check are widened by one additive migration generated with the EF CLI.
- API: `POST Pacs004RestApiRoutes.Send` with `Pacs004RequestMapping`; the status route already covers the type. `OutgoingRuntime` routes the new request type to `Pacs004Intake`.
- Configuration: the same `Payments:Outgoing:Policy`, `Protocol` and `Investigation` sections; no new keys.

## Acceptance

Real SQL and independently signed IPS fixtures:

- Validation: every rule above, including a reason code other than FOCR, a returned amount over the original, a missing original reference, parties without name or with a bad IBAN, a non-participant instructing agent and an unconfigured indirect participant; normalization and defaults.
- Intake: duplicate client reference, reused id, race on the unique indexes.
- XML: schema validity, the exact element set and order, `OrgnlGrpInf` only when both ids are given, original amount defaulting to the returned amount, `ClrSysMmbId` for an indirect participant, never-sent fields absent, signature verified independently, byte-stable resume at each checkpoint.
- Outcomes: accepted, rejected, unknown; a reply that echoes the original transaction and end-to-end ids is accepted and one that echoes other ids is not; callback created; 200, 504 and immediate duplicate status over HTTP.
- Recovery: flagged resend of the exact original, backoff, window, attempt cap, abandoned marker, saved reply replay, competing and stale owners, process kills at ready, marker and response.
- An unsolicited pacs.002 about a pacs.004 settles it.
- pacs.008 and pacs.009 behavior is unchanged: the full existing suite passes with only mechanical updates listed in the review.
- Build, full tests, format, EF model and migration chain, independent Standards and Spec reviews.

## Implementation notes (deviations from the first draft)

- The source mapper never sends the transaction-level `IntrBkSttlmDt` (the draft listed it) or `OrgnlIntrBkSttlmDt`; the settlement date appears only in the group header and, as the original value date, in `OrgnlTxRef`. The message follows the mapper and the schema.
- `AcceptedPacs004.EndToEndId` is the returned payment's end-to-end id, so the status of a return shows the original payment's; the reply correlation uses the same original ids.
- Party fields are a nested `Pacs004PartyInput` (type, name, identifier, account); the identifier and its kind are kept together or not at all.
- The shared `PartyValidator` of pacs.008 requires a tax code for resident organisations and a different account rule per side; returns follow the source's own simpler rules (name, IBAN, optional identifier with a type), so pacs.004 has its own small party validator.
- `Pacs004ProtocolProfile` is the same shape as pacs.009's. Two copies are kept until a third consumer exists; extract then.
- Process-kill tests for pacs.004 cover ready, marker, response and a resend response; the remaining checkpoints run the same shared code and are covered for pacs.009.

## Not in scope

Incoming pacs.009/004, recalls and their answers, pain.001 initiation, real IPS interoperability (the reply correlation and the flagged-resend behavior rest on the source and its mock), production activation.
