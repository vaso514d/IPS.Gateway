# Review 008a: incoming pain.001 (payment initiation)

Status: approved by the owner on 2026-10-07 and implemented on codex/payment-initiation, stacked on 007c (a951787) and the earlier unmerged slices 005a-005d and 007a-007b (merge approval pending for all of them).

## Scope

Receive a pain.001 payment initiation that a payment initiation service provider (PISP) submits through IPS for an account of ours, verify it, hand it to the core system (CBS) once, recover an unanswered hand-over, and acknowledge it to IPS. The core system answers IPS itself: it accepts by sending a pacs.008 whose EndToEndId is `PSP-` plus the initiation's `PmtInfId` (already possible with the outgoing pacs.008), or refuses with a pain.002 (008b). It reuses the incoming transfer engine of [005c](005c-incoming-pacs009.md) and [005d](005d-incoming-pacs004.md). No Contracts change: `IClientPaymentReceiver.ReceivePain001Async`, `Pain001RestApiRoutes.Receive` and `Pain001PaymentInitiationDto` exist. Outgoing pain.002 (008b), camt.055 (008c), linking a later `PSP-` pacs.008 to the stored initiation, and the initiation deadline stay out of scope.

## Source evidence

Pinned source d498de6c4638aa71cdb20189d13642b41abab5f1: `Gateway/Services/Pain001InboundPaymentMapper.cs`, `CoreApiInboundMessageHandler.HandlePain001Async`, `InboundCoreCaller`, `InboundCoreReconciliation`, `InboundAuditWriterService.AddPain001Async`, `Application/Inbound/InboundPain001Payment.cs`, `CoreReferences`, `Contracts/Pain001/Pain001PaymentInitiationDto.cs`, `Contracts/Abstractions/IClientPaymentReceiver.cs` (the receive method's contract: the core answers IPS itself before the Timeout Deadline counted from `creationDateTime`), `docs/IPS_Seq_pain001_KA.drawio`.

- The receipt is stored and `MessageAck` is sent right after; IPS gets nothing else from us for the initiation. The core's ACCP or RJCT is audit-only.
- The mapper reads one `PmtInf` with one `CdtTrfTxInf`: `GrpHdr` (`MsgId`, `CreDtTm` kept with its own offset because the accepting pacs.008 repeats it as `AccptncDtTm`, `InitgPty` name and `AnyBIC`), `PmtInfId`, `PmtTpInf` (service level, local instrument, category purpose), `ReqdExctnDt` (date or date-time), the debtor, debtor account, debtor agent and ultimate debtor of the `PmtInf`, and in the transaction `InstrId`, `EndToEndId`, the instructed amount and currency, the creditor, creditor account and agent, ultimate creditor, purpose code and remittance. Parties, accounts, agents, addresses and remittance are read exactly as for an incoming pacs.008.
- Identity: `PmtInfId` (the PISP's `EndToEndId` is not the key; the pacs.008 that pays the initiation uses `PSP-` + `PmtInfId`). The core idempotency key is `PmtInfId ?? EndToEndId ?? MsgId`.
- No schema, signature or business validation; a mapper failure is swallowed. Delivery is one POST to `Pain001ReceivePath`; there is no deadline pressure on the call and a failure leaves the transaction unknown.
- Recovery is the shared reconciliation: ask the core (`messageKind=Pain001`), ACCP or RJCT settles, a 404 re-posts the stored request with the same key, retries on a backoff, 24-hour window, then manual review.

## Owner decisions (2026-10-07)

1. **Slicing.** 008a incoming pain.001 first, then 008b outgoing pain.002, then 008c camt.055; linking a `PSP-` pacs.008 is an optional later slice.
2. **A `PmtInfId` longer than 31 characters is held and delivered to nobody.** `PSP-` plus it would not fit the 35-character EndToEndId of the accepting pacs.008, so the initiation could only be refused, which needs 008b. IPS rejects it on its own deadline.

Carried from 005c and 005d: MessageAck only after the receipt commits (unverified against Annex D); validation is schema, trusted IPS signature, one initiation and mandatory fields, anything unverifiable is held; the key is the initiation's own id; no link to other payments; an absent date stays absent.

## Design

- **Application `Inbound/Transfers/`.** `IncomingPain001` (a record implementing `IIncomingTransferContent`): group message id, creation time (with offset), initiating party, `PmtInfId`, service level, local instrument, category purpose, requested execution date (optional), debtor with account, agent and ultimate party, instruction id, end-to-end id, amount, currency, creditor with account, agent and ultimate party, purpose code and remittance. The debtor, creditor, ultimate-party and remittance parts reuse the existing `Pacs008DebtorInput`, `Pacs008CreditorInput`, `Pacs008UltimatePartyInput` and `Pacs008RemittanceInput` records. Kind `pain.001`, key `PmtInfId`, receiver the debtor agent (`PmtInf/DbtrAgt`, which must be our participant), echo `Id` = `PmtInfId`.
- **Message types.** `PaymentMessageTypes` gains `Pain001` and `Pain001Definition` and an `IsPain001` matcher; `IncomingReceiptPreparation` routes pain.001 receipts to `IncomingTransferRegistration`; `IncomingReceiveWorker` acknowledges them after the receipt commit. The registration, processing, repository, discovery and recovery are unchanged apart from reading and writing the new kind.
- **Reading (`IncomingPain001Protocol`).** Envelope, definition `pain.001.001.12` (the Annex D text also shows `pain.001.001.012`; the protocol accepts the registered definition and the schema decides), trusted signature, one `PmtInf` with one `CdtTrfTxInf`, schema validation, and the mandatory fields: `PmtInfId` of at most 31 characters (otherwise held with that reason), `GrpHdr/MsgId`, a valid `CreDtTm`, the debtor agent by BICFI, amount and currency. Field rules follow the source mapper; the shared party, address and remittance reading of the incoming pacs.008 mapping is made reusable instead of copied (the debtor is read from `PmtInf`, the creditor from the transaction).
- **Core call.** `Pain001SubmissionPath` (default `Pain001RestApiRoutes.Receive`) in `IncomingTransportSettings`, `appsettings.json` and `docs/configuration.md`; `IncomingCbsClient` maps `IncomingPain001` to `Pain001PaymentInitiationDto` (no client reference) with the `PmtInfId` as idempotency key and status reference (`messageKind=Pain001`). A core reply is accepted if the `Id` it names is the `PmtInfId`. The core's late answer has no effect on IPS here; the initiation deadline is not enforced by us.
- **Persistence.** The stored content is read and written by kind (`IncomingTransferJson`); the key column already holds a `PmtInfId`. One migration is generated with the EF CLI only if the kind column or a constraint needs widening; otherwise none.
- **Tests.** No existing test uses `pain.001` as an unsupported type.

## Acceptance

Real SQL and independently signed IPS fixtures:

- Reader: a valid initiation, optional parts (ultimate parties, remittance, requested execution date as date or date-time, purpose and category codes, non-IBAN accounts), each unverifiable case (wrong definition, bad schema, wrong or missing signature, several payment instructions, missing `PmtInfId`, missing `MsgId` or creation time, an agent without BICFI, a debtor agent that is not us) and a `PmtInfId` of 32 characters held while one of 31 is accepted.
- Identity: a redelivery is one transfer; different content under the same `PmtInfId` is held; the same text as a pacs.009 key or a return id is a separate transfer.
- Delivery and recovery: the `PmtInfId` as key and reference, ACCP and RJCT, a reply naming another id refused, 404 re-post of the same request, backoff and window end (the shared engine is covered by the earlier slices).
- Worker: pull, store, acknowledge and hand a pain.001 to the core over HTTP, with its status asked as `Pain001`.
- pacs.008, pacs.009, pacs.004, recall and outgoing behavior is unchanged: the full existing suite passes with only mechanical updates listed in the review.
- Build, full tests, format, EF model and migration chain, independent Standards and Spec reviews.

## Not in scope

Outgoing pain.002 and the reply interpretation for it (008b), camt.055 (008c), enforcing or tracking the initiation's Timeout Deadline, linking a `PSP-` pacs.008 to the stored initiation, a pain.002 sent by IPS to us, real IPS interoperability (the ack-only behavior, the key and the definition text rest on the source), production activation.
