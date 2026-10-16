# Review 005d: incoming pacs.004

Status: approved by the owner on 2026-10-06 and implemented on codex/incoming-pacs004, stacked on codex/incoming-pacs009 (87bbaf7), codex/outgoing-pacs004 (e65c3a5) and codex/outgoing-pacs009 (fc82368); none of those merges is approved yet.

## Scope

Receive a pacs.004 (payment return) that IPS sends us, verify it, hand it to the core system (CBS) once, recover an unanswered hand-over, and acknowledge it to IPS. It reuses the incoming transfer engine of [005c](005c-incoming-pacs009.md), generalised so one aggregate, repository, processing and worker serve both message types. No Contracts change (`IClientPaymentReceiver.ReceivePacs004Async`, `Pacs004RestApiRoutes.Receive`, `Pacs004PaymentReturnRequestDto` and `IpsMessageKind.Pacs004` exist). Recalls (camt.056/029) and initiation stay separate.

## Source evidence

Pinned source d498de6c4638aa71cdb20189d13642b41abab5f1: `Gateway/Services/Pacs004InboundPaymentMapper.cs`, `CoreApiInboundMessageHandler.HandlePacs004Async`, `InboundCoreCaller`, `InboundCoreReconciliation`, `InboundAuditWriterService.AddPacs004Async`, `Application/Inbound/InboundPacs004Payment.cs`, `CoreReferences`.

- The receipt is stored and `MessageAck` is sent right after; IPS gets no pacs.002.
- The mapper reads the first `TxInf` only: `Id` = `GrpHdr/MsgId` (else `BizMsgIdr`; `RtrId` is never read); amount `RtrdIntrBkSttlmAmt`; value date `TxInf/IntrBkSttlmDt` else group date else today; the original block (`OrgnlTxId` required, `OrgnlInstrId`, `OrgnlEndToEndId`, `OrgnlUETR`, `OrgnlGrpInf/OrgnlMsgId`, `OrgnlMsgNmId`, `OrgnlCreDtTm`, `OrgnlIntrBkSttlmAmt` with its currency, original date from `OrgnlTxRef/IntrBkSttlmDt` else `OrgnlIntrBkSttlmDt`); instructed agent `InstdAgt` else `OrgnlTxRef/DbtrAgt`; instructing agent `InstgAgt` else `OrgnlTxRef/CdtrAgt`; debtor and creditor from `OrgnlTxRef` (IBAN else `Othr/Id`); sender member id `OrgnlTxRef/CdtrAgt/…/MmbId`; reason `RtrRsnInf/Rsn/Cd` else `Prtry`; `RtrRsnInf/AddtlInf`.
- No schema, signature or business validation; a mapper failure is swallowed.
- Delivery is one POST to `Pacs004ReceivePath`; the core key is the return id (`CoreReferences.For` = `Id ?? Original.EndToEndId`), although the handler passes the original EndToEndId to `SendAsync` (inconsistent in the source). Recovery is the shared reconciliation: ask the core (`messageKind=Pacs004`), re-post on 404, 24 h window, manual review. Nothing links the return to the original outgoing payment.

## Owner decisions (2026-10-06)

1. **Storage: generalise the transfer aggregate.** `IncomingFiTransfer` becomes `IncomingTransfer` with a `Kind` (`pacs.009`, `pacs.004`) and a business `Key` (EndToEndId or return id); unique on participant BIC, kind and key. The state machine, repository, processing, discovery and events are shared. 005c is not merged, so its types are renamed on this stacked branch, and one new migration (EF CLI) reshapes the table.
2. **Identity: the return id.** A return is keyed by participant BIC and `RtrId`, which must equal `GrpHdr/MsgId` (otherwise the receipt is held). The return id is also the CBS idempotency key and status reference. This is stricter than the source and rests on its own comment that one payment can have several returns; the core owner should confirm the key.
3. **No link to the original outgoing payment**, as in the source. The original ids go to the core and are recorded with the content; the outgoing payment is untouched.

Carried from 005c: MessageAck only after the receipt commits (unverified against Annex D); validation is schema, trusted IPS signature, one transfer and mandatory fields; delivery and recovery as in 005c.

## Design

- **Domain.** `IncomingTransfer` replaces `IncomingFiTransfer`: `Kind` and `Key` replace the pacs.009 `EndToEndId`; state machine, `CoreOutcome` and events are unchanged except that the registered event names the kind and key.
- **Application `Inbound/Transfers/`.**
  - `IIncomingTransferContent` (`Kind`, `Key`, `ReceiverBic`, the identifiers a core reply may echo) implemented by the records `IncomingPacs009` and the new `IncomingPacs004`.
  - `IIncomingTransferProtocol` (one per kind, selected by message type like `IOutgoingMessageProtocol`) reads and verifies the XML into `IncomingTransferReadResult` (Ready or Hold).
  - `IncomingTransferRegistration` (was `IncomingPacs009Processing`) registers any kind: read, require our BIC as `ReceiverBic`, find by (BIC, kind, key), same content completes, different content holds as a conflict.
  - `IncomingTransferProcessing`, `IIncomingTransferRepository` and the core client work on `IIncomingTransferContent`. `IncomingReceiptPreparation` routes `IsPacs009` and `IsPacs004` receipts to the registration.
- **`IncomingPacs004`** holds: return id, amount, currency, optional value date (absent stays absent, as in 005c), reason code and additional text as received (no FOCR check on an incoming return), instructed and instructing agent BICs, the sender member id, debtor and creditor (name, optional kind and identifier, account), and the original block. Receiver = the instructed agent, `OrgnlTxRef/DbtrAgt`; it must be our participant.
- **Reading (`IncomingPacs004Protocol`).** Envelope, definition `pacs.004.001.13`, trusted signature, then one `TxInf` with `NbOfTxs` 1, `Pacs008Schema.ValidatePacs004`, and the mandatory fields: `RtrId` equal to `GrpHdr/MsgId`, `OrgnlTxId`, both agents by BICFI. Field rules follow the source mapper and the outgoing builder of [005b](005b-outgoing-pacs004.md), which is the profile IPS forwards.
- **Core call.** `Pacs004SubmissionPath` (default `Pacs004RestApiRoutes.Receive`); the DTO maps from `IncomingPacs004` with no client reference; the idempotency key and status reference are the return id; the status query takes the content's kind. A core reply is accepted only if its `Id`, when named, is the return id. The core's echo of the original ids is unknown, so those are not checked; for both kinds an identifier the content does not carry is no longer compared (a small relaxation of 005c, which compared a missing pacs.009 transaction id).
- **Ack.** `IncomingReceiveWorker` acknowledges a pacs.004 after the receipt commit like the other types.
- **Persistence.** One migration generated with the EF CLI: kind column, key column (renamed from the pacs.009 EndToEndId), unique index on (BIC, kind, key bytes), event and identity constraints unchanged. The stored request JSON is read by kind.
- **Configuration.** `Pacs004SubmissionPath` in `appsettings.json` and `docs/configuration.md`.

## Implementation notes

- Agents are read as the source does: the group-level `InstdAgt` and `InstgAgt` when present, else `OrgnlTxRef/DbtrAgt` and `CdtrAgt`; a transaction-level agent is not used. Only the first `RtrRsnInf/AddtlInf` is kept.
- The new migration renames the pacs.009 key column and adds the kind column with an empty default, which is only valid because 005c was never deployed; a database that ran 005c needs its rows set to `pacs.009` first.

## Acceptance

Real SQL and independently signed IPS fixtures:

- Reader: valid return and its alternative wire forms (member id, `Othr` accounts, `Prtry` reason, group-only date, missing optional original fields); wrong definition, bad schema, wrong or missing signature, several transfers, `RtrId` missing or different from `MsgId`, missing `OrgnlTxId`, an agent without BICFI, receiver not ours: each Held with a reason, nothing delivered.
- Identity: a redelivery is one transfer; different content under the same return id is held; two returns of the same original payment are two transfers; a pacs.009 and a pacs.004 with the same key are two transfers.
- Delivery and recovery: everything 005c covers, for a return (submission with the return id as key, ACCP and RJCT, a reply naming another id refused, backoff, 404 resubmission, window end, competing and stale owners, crash recovery), plus the relaxed identifier comparison for both kinds.
- Worker: pull, store, acknowledge and hand a pacs.004 to the core over HTTP, with the pacs.009 test unchanged apart from names.
- pacs.008 and outgoing behavior unchanged: the full existing suite passes with only the mechanical updates listed in the review (renamed types in the 005c tests).
- Build, full tests, format, EF model and migration chain, independent Standards and Spec reviews.

## Not in scope

Linking a return to its outgoing payment, reversals, camt.056/029, any pacs.002 for a pacs.004, real IPS interoperability (the ack-only behavior and the core's key rest on the source), production activation.
