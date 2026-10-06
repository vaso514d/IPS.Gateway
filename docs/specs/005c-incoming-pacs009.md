# Review 005c: incoming pacs.009

Status: approved by the owner on 2026-10-06 and implemented on codex/incoming-pacs009, stacked on codex/outgoing-pacs004 (e65c3a5), which is stacked on codex/outgoing-pacs009 (fc82368); neither merge is approved yet.

## Scope

Receive a pacs.009 that IPS sends us, verify it, hand it to the core system (CBS) once, recover an unanswered hand-over, and acknowledge it to IPS. No Contracts change (`IClientPaymentReceiver.ReceivePacs009Async`, `Pacs009RestApiRoutes.Receive` and the DTO exist). Incoming pacs.004 (005d), recalls and initiation stay separate.

## Source evidence

Pinned source d498de6c4638aa71cdb20189d13642b41abab5f1: `Gateway/Services/CoreApiInboundMessageHandler.HandlePacs009Async`, `IpsInboundReceiverService`, `Pacs009InboundPaymentMapper`, `InboundTransactionRecorder`, `InboundAuditWriterService.AddPacs009Async`, `InboundCoreCaller`, `InboundCoreReconciliation`, `Application/Inbound/InboundPacs009Payment.cs`.

- The receipt is stored under participant BIC plus IPS sequence, and `MessageAck` is sent right after that. IPS gets no pacs.002 for a pacs.009; the core's answer is only recorded.
- The mapper builds `Pacs009PaymentRequestDto`: `Id` = `GrpHdr/MsgId` (else `BizMsgIdr`), agents from `DbtrAgt`/`CdtrAgt` (else `Dbtr`/`Cdtr`) as BICFI and `ClrSysMmbId/MmbId`, `InstrId`, `EndToEndId`, `TxId`, `UETR`, priority, `IntrBkSttlmDt`, amount and currency, category purpose and purpose as code or proprietary, unstructured remittance joined, accounts as IBAN else `Othr/Id`. No schema, signature or business validation, and a mapper failure is swallowed.
- Delivery is one POST to `Pacs009ReceivePath` with idempotency key `EndToEndId ?? TxId ?? Id`. A failure leaves the transaction unknown; there is no deadline pressure.
- Recovery: ask the core (`GET status?messageKind=Pacs009&reference=`); ACCP/RJCT settle with source Core; PDNG or a failed read retries; a 404 re-posts the stored request with the same key. Delays 10 s, 30 s, 1 min, 5 min, then 15 min; 24 h window, then manual review. (This rebuild reuses the incoming reconciliation options instead, so its delays are 30 s, 1 min, 5 min, then 15 min.) A message stuck in Received for 5 minutes becomes unknown and is reconciled. No reversal path.

## Owner decisions (2026-10-06)

1. **IPS reply: MessageAck only**, after the receipt is durably stored, exactly as for the status reports of 003a.4. No pacs.002 and no reply workflow. Whether IPS expects more for a pacs.009 is unverified against Annex D.
2. **Storage: a separate lightweight aggregate**, `IncomingFiTransfer`, with its own table and one migration. It has no IPS decision and no reversal. The pacs.008 aggregate, key and tables are untouched.
3. **Validation: schema, trusted IPS signature and mandatory fields**, like pacs.008. Anything unverifiable is Held with a reason and nothing is delivered. The only business rule is that the creditor agent is our participant BIC.

## Design (defaults for owner review)

- **Identity.** One transfer per participant BIC and EndToEndId (binary collation), like pacs.008. A redelivery with the same content is a duplicate; different content under the same key is Held as a conflict. The CBS idempotency key is `EndToEndId`, which is always present after validation (the source's fallbacks only covered parse gaps).
- **Value date.** A value date IPS did not send stays absent (the source filled in the current day, which would make a redelivery on another day look like a conflict); the core receives it empty.
- **Domain.** `IncomingFiTransfer` records the received content, the core hand-over state (Received, Delivering, Delivered with the core's final ACCP or RJCT, Unknown, ManualReview), a source (Core), and events. Its outcome is only what the core said; nothing is decided toward IPS.
- **Application `Inbound/Transfers/`.** `IncomingPacs009Processing` (concrete handler: register, deliver, settle), reached from `IncomingReceiptPreparation` by a new `IsPacs009` branch next to pacs.002; unsupported types stay Held. `IIncomingPacs009Protocol` is the port that reads and verifies the XML (Infrastructure implements it).
- **Core call.** Reuse `CoreCall`/`CoreCallExecution` and the generic parts of `IncomingCbsClient` through a typed submit for `Pacs009PaymentRequestDto`; `IncomingTransportSettings` gains `Pacs009SubmissionPath` (default `Pacs009RestApiRoutes.Receive`). The status query takes the message kind (`Pacs009`) instead of the hard-coded `Pacs008`. A reply is accepted only if it echoes the transfer's identifiers. There is no IPS deadline, so only the core call timeout applies; an unanswered call stays Unknown.
- **Recovery.** A due-work sweep (same discovery pattern as incoming pacs.008) finds Unknown and long-Received transfers and asks the core; ACCP/RJCT settles, a 404 re-posts the stored request with the same key, anything else retries on 10 s, 30 s, 1 min, 5 min, then 15 min; at 24 hours the transfer goes to ManualReview. Timings reuse the existing incoming options where they already exist.
- **Ack.** `IncomingReceiveWorker` acknowledges a pacs.009 after the receipt commit (and a duplicate again), through the existing `IIncomingAckClient`, never blocking polling.
- **Infrastructure `Inbound/Transfers/`.** `IncomingPacs009Protocol` (envelope, `MsgDefIdr` `pacs.009.001.11`, XSD, IPS signature, single transfer), `IncomingPacs009Mapping` to `Pacs009PaymentRequestDto` using the source's field rules, persistence configuration and repository for the aggregate, one EF-CLI migration.
- **Configuration.** Existing `Payments:Incoming` sections plus the one new path; documented in `docs/configuration.md`.
- Nothing is added to the API surface: incoming messages arrive through the pull, as for every other inbound type.

## Acceptance

Real SQL and independently signed IPS fixtures:

- Reader: valid message; wrong definition, bad schema, wrong or missing signature, several transfers, missing mandatory fields, creditor agent not ours: each Held with a reason, nothing delivered.
- Mapping: every field rule above, including agents with and without member id, IBAN versus other account, code versus proprietary purposes, remittance lines, value date.
- Flow: stored, acknowledged after commit, delivered once with the idempotency key, ACCP and RJCT recorded, a core reply echoing other ids not accepted; redelivery acknowledged again and delivered once; conflicting content Held.
- Recovery: unanswered call becomes Unknown and is reconciled; 404 re-posts the same bytes and key; PDNG retries on the schedule; the 24-hour end goes to manual review; stuck-Received rule; competing and stale owners; crash after each commit resumes without a second delivery.
- Worker: pull to settled end to end; ack failure does not stop polling; non-positive sequence handled as for the other types.
- pacs.008 incoming and outgoing behavior unchanged: the full existing suite passes with only mechanical updates listed in the review.
- Build, full tests, format, EF model and migration chain, independent Standards and Spec reviews.

## Not in scope

Incoming pacs.004, any pacs.002 for a pacs.009, reversals, real IPS interoperability (the ack-only behavior and the idempotency key rest on the source), production activation.
