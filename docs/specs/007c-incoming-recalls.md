# Review 007c: incoming camt.056 and camt.029 (acknowledge and archive)

Status: approved by the owner on 2026-10-07 and implemented on codex/recalls, stacked on 007b (c5b63cf) and the earlier unmerged slices 005a-005d (merge approval pending for all of them).

## Scope

An incoming camt.056 (a recall request another bank sends us) and an incoming camt.029 (a refusal of a recall we sent) are acknowledged to IPS after the receipt is stored, and the receipt is completed as archived. Nothing is delivered to the core system and nothing is tracked. This replaces the current behavior, where such receipts are held as an unsupported type and never acknowledged, so IPS redelivers them. Delivering them to the core (a new Contracts receive method, DTOs and routes), correlating a camt.029 with the recall it answers, and every other message type stay out of scope. Incoming pacs.004 (005d) already carries a positive answer to a recall.

## Source evidence

Pinned source d498de6c4638aa71cdb20189d13642b41abab5f1: `Gateway/Services/CoreApiInboundMessageHandler.cs` (`HandleByTypeAsync`: pacs.008, pacs.009, pacs.004 and pain.001 have handlers; "every other type is only confirmed with MessageAck; nothing is told to the core system"), `Domain/ValueObjects/IpsMessageType.cs` (`Camt056` and `Camt029` have `requiresExplicitAck: true`), `docs/IPS_Seq_camt056_KA.drawio` and `IPS_Seq_camt029_KA.drawio` (the core is not informed, "to be done"). The XML is stored once with its receipt, no schema, signature or business check is made, and a redelivery is acknowledged again.

## Owner decision (2026-10-07)

**Acknowledge and archive, as the source.** No Contracts change. The gap is known and recorded: the core is not told of a recall request, so a recall is answered only when a person or process sends a pacs.004 (accept) or a camt.029 (refuse) on its own. Closing it later needs a Contracts receive method and an owner decision.

## Design

- **Message types.** `PaymentMessageTypes` gains `Camt056` and `Camt029` matchers for incoming receipts (short type or full definition) and one `IsArchivedRecall` rule that names both. The outgoing set is unchanged.
- **Preparation.** After the sequence check, `IncomingReceiptPreparation` completes a recall receipt as processed with no further work, before the unsupported-type hold. A missing or non-positive sequence still holds, as for every type. No payment, transfer or reply is created, and no remote system is called.
- **Acknowledgement.** `IncomingReceiveWorker` acknowledges a camt.056 and a camt.029 after the receipt commit, through the existing `IIncomingAckClient`, never blocking polling; a redelivery is acknowledged again. Other unsupported types stay unacknowledged and held.
- **No verification.** Like the source, the message is neither schema-checked nor signature-verified, because nothing is acted on; the stored XML is the archive. This is recorded as a deliberate limit: anything later built on these messages must verify them first.
- **Tests.** The two existing tests that use `camt.056` as an unsupported incoming type switch to `camt.053`.

## Acceptance

Real SQL and the existing scripted receiver:

- A camt.056 and a camt.029 receipt (by short type and by full definition) complete as processed, create no payment, transfer or reply, and make no remote call; the receipt keeps its raw XML.
- A missing or non-positive sequence holds the receipt.
- The worker acknowledges both types after the commit with the sequence header, also when a previous acknowledgement failed (redelivery acknowledges again), and does not acknowledge `camt.053`, which stays held as unsupported.
- A duplicate sequence is processed once.
- pacs.008, pacs.009, pacs.004 and pacs.002 incoming behavior is unchanged: the full existing suite passes with only the mechanical stand-in change listed above.
- Build, full tests, format, independent Standards and Spec reviews. No migration.

## Not in scope

Delivering recalls to the core, any Contracts change, correlating a camt.029 with its recall, signature or schema verification of these messages, camt.055, pain.001, pain.002, pain.013/014, camt.053 and other types, real IPS interoperability (that IPS wants only an acknowledgement rests on the source).
