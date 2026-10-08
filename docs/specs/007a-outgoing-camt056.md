# Review 007a: outgoing camt.056 (request for recall)

Status: approved by the owner on 2026-10-06 and implemented on codex/recalls, stacked on codex/incoming-pacs004 (b2e090f) and the earlier unmerged slices 005a-005c (merge approval pending for all of them).

## Scope

Add the outgoing camt.056 end to end on the shared outgoing core from [005a](005a-outgoing-pacs009.md) and [005b](005b-outgoing-pacs004.md): intake and validation, XML, signing, `POST /api/ips/camt056/send`, send, outcome, CBS callback and possible-duplicate recovery. A recall asks IPS to recall a completed outbound pacs.008 this bank sent. Outgoing camt.029 (007b), incoming camt.056/camt.029 (007c, 007d) and camt.055 (with pain.001 initiation) stay separate. The Contracts DTOs and route exist; only the approved metadata edit below touches Contracts.

## Source evidence

Pinned source d498de6c4638aa71cdb20189d13642b41abab5f1: `Contracts/Camt056/Camt056RecallRequestDto.cs`, `Application/Validation/RecallRequestValidators.cs`, `API/Services/RecallXmlMessageBuilder.cs`, `API/Transactions/IsoTransactionSenders.cs` (`Camt056TransactionSender`), `MockIps/Validators/RecallRequestValidator.cs`, `Tests/Ips/Schemas/camt.056.001.11.xsd`, and the docs `IPS_Seq_camt056_KA.drawio` and `Camt055_Camt056_Client_Model_Reference_KA.md`.

- Route `POST /api/ips/camt056/send`, 202 in the source. The business answer comes later and separately: an inbound pacs.004 (funds returned, already handled by 005d) or an inbound camt.029 (refused). The gateway neither correlates nor models that answer.
- Message id = `Id`; the recall id is `TxInf/CxlId`. IPS answers synchronously with a technical pacs.002: ACCP means accepted and forwarded, RJCT rejected. That verdict is the transaction's final status; nothing else is tracked.
- Validation: ClientReference; `Id`, `RecallId`, `OriginalMessageId`, `OriginalEndToEndId`, `OriginalTransactionId` required and 35 ASCII; original currency enabled; original amount greater than zero; original settlement date required and not in the future; reason code required, 1-4 uppercase letters or digits; `OriginalTransaction` required with its settlement date (required, not in the future), remittance (unstructured up to 140; creditor reference with reference up to 35 and optional issuer), optional ultimate debtor and creditor (name up to 140, id with type 0 or 1), debtor and creditor required (name, optional postal address within the XSD lengths and at most seven lines, optional id with type, IBAN with checksum), debtor agent and creditor agent required as BICFI with optional name, and **the debtor agent must be our participant** (Annex D §3.2.4.f).
- XML `camt.056.001.11` (`FIToFIPmtCxlReq`): `Assgnmt` (`Id` = message id, `Assgnr` = our BIC, `Assgne` = IPS BIC, `CreDtTm` = the caller's or now), `Undrlyg/TxInf` with `CxlId`, `OrgnlGrpInf` (original message id; name id fixed to `pacs.008.001.12`), original end-to-end id, original transaction id, `OrgnlIntrBkSttlmAmt`, `OrgnlIntrBkSttlmDt`, `CxlRsnInf` (originator `AnyBIC` = our BIC, reason code) and `OrgnlTxRef` (settlement date, `SttlmInf` CLRG with `ClrSys` IPS, `PmtTpInf` SvcLvl and LclInstrm, remittance with `SCOR` for a creditor reference, ultimate debtor, debtor with optional address and id, IBAN accounts, both agents, creditor, ultimate creditor), in XSD order.
- No pre-send deadline. A resend after an unknown outcome is a possible duplicate, as for pacs.009 and pacs.004. A pacs.002 reply echoes the original payment's end-to-end id and transaction id and our message id (from the source's mock IPS).
- Nothing in the source looks up the original payment, limits the recall's age, or lists allowed reason codes.

## Owner decisions (2026-10-06)

1. **Slicing.** Recalls are 007a outgoing camt.056, then 007b outgoing camt.029, then incoming camt.056/camt.029 (007c, 007d, which need a new Contracts receive method and a decision on delivering them to the core). This review is 007a.
2. **HTTP result: 200 with the final status or 504 with the current status**, duplicates return the current status at once, as for the other types. The stale 202 declarations on `IGatewayApi` (pacs.009, pacs.004, camt.056, camt.029) are corrected: only each method's `SuccessStatusCode` and description change, following the Stage 2c.2b exception recorded in the baseline README, with `public-api.json` edited explicitly for those methods (not regenerated) and no wire or type change. pain.002 stays 202 until it is built.
3. **Reason code: the source pattern, no lookup.** Any 1-4 uppercase letter or digit code is accepted and nothing is checked against the original payment. A list of allowed codes can be added later if the owner supplies Annex D's.

## Design

Application `Payments/Camt056/`: `Camt056Request` (record with the original transaction block and its parties as nested input records), `Camt056Validator`, `ValidatedCamt056`, `AcceptedCamt056` and `Camt056Intake`. The IPS BIC and service-level settings are the shared `IpsMessageProfile`, extracted from the identical pacs.009 and pacs.004 profiles when camt.056 became the third consumer.

- **Identifiers.** Message id = `Id`, transaction id = `RecallId`, both caller-chosen and unique across payments; reuse is a validation error and the unique indexes fence a race. The status shows the original end-to-end id (`IAcceptedPayment.EndToEndId`).
- **Reply correlation** (`IAcceptedPayment.ReplyCorrelation`): our message id, the original transaction id, the original end-to-end id and the definition `camt.056.001.11`. Real IPS behavior is unverified.
- **Validation.** The rules above. Two rules need today's date; the validator takes it as a value from the intake's time provider, so it stays pure. The shared amount-precision rule applies to the original amount. Party and postal-address rules reuse the shared protocol-text patterns; the source's separate recall party rules (name, optional id with type, IBAN) are small and live with the recall validator.
- **`PaymentMessageTypes`** gains `Camt056`, `Camt056Definition` (`camt.056.001.11`) and joins the outgoing set; `HasInvestigation` stays pacs.008-only, so recovery is the shared possible-duplicate resend.
- **Infrastructure `Payments/Camt056/`.** Embedded `camt.056.001.11` schema, `Camt056Message`, `Camt056Xml`, `Camt056Preparation : IOutgoingMessageProtocol`, a signer overload and `Pacs008Schema.ValidateCamt056`; registered with the other protocols.
- **Persistence.** The accepted snapshot JSON reads by message type; the type and message-definition check constraints are widened by one additive migration generated with the EF CLI.
- **API.** `POST Camt056RestApiRoutes.Send` with `Camt056RequestMapping`; the status route already covers the kind. `OutgoingRuntime` routes the new request type to `Camt056Intake`.
- **Tests.** Existing tests that use `camt.056` as an unsupported message type switch to a type the rebuild never sends (`pacs.003`).
- **Configuration.** Same `Payments:Outgoing` sections; no new keys.

## Acceptance

Real SQL and independently signed IPS fixtures:

- Validation: every rule above, including a settlement date in the future, a debtor agent that is not our participant, a missing original block, a bad IBAN, an over-long address line, a reason code of five characters or lowercase, an amount with too many decimals; normalization.
- Intake: duplicate client reference, reused id or recall id, race on the unique indexes.
- XML: schema validity, the exact element set and order, name id fixed to `pacs.008.001.12`, `SCOR` only with a creditor reference, optional parts omitted, signature verified independently, byte-stable resume at each checkpoint.
- Outcomes: accepted, rejected, unknown; a reply that echoes the original transaction and end-to-end ids is accepted and one that echoes other ids is not; callback created; 200, 504 and an immediate duplicate over HTTP.
- Recovery: flagged resend of the exact original bytes, backoff, window, attempt cap, abandoned marker, saved reply replay, competing and stale owners, process kills at ready, marker and response (the shared engine is covered by the pacs.009 and pacs.004 tests; recall-specific cases cover correlation and bytes).
- An unsolicited pacs.002 about a camt.056 settles it.
- pacs.008, pacs.009, pacs.004 and incoming behavior is unchanged: the full existing suite passes with only the mechanical updates listed in the review; the public API baseline differs only in the four edited route declarations.
- Build, full tests, format, EF model and migration chain, independent Standards and Spec reviews.

## Not in scope

Linking a recall to the payment it recalls or to its later pacs.004 or camt.029 answer (the source has no such state), a recall time limit, an Annex D reason list, outgoing camt.029, incoming recalls and their delivery to the core, camt.055, real IPS interoperability (the reply correlation rests on the source and its mock), production activation.
