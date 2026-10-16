# Specification 008c: incoming camt.055 (acknowledge and archive)

Status: specification on codex/payment-initiation, stacked on 008b (1f2926f) and the earlier unmerged slices 005a-005d and 007a-007c (merge approval pending for all of them). The owner approved it on 2026-10-07; implemented, review pending.

## Scope

An incoming camt.055 is a PISP's request to cancel a payment initiation (pain.001, 008a) or to ask for the status of an earlier cancellation request, forwarded by IPS to us as the originator participant (Annex D 3.2.13, Figure 25). It is acknowledged to IPS after the receipt is stored and the receipt is completed as archived, exactly as 007c does for camt.056 and camt.029. Nothing is delivered to the core system, nothing is tracked, no pacs.002 or camt.029 answer is produced. Delivering it to the core (a Contracts receive method), answering the cancellation (a camt.029 about a pain.001), linking it to the stored initiation, pain.013/pain.014 and every other message type stay out of scope. There is no outgoing camt.055: the PISP sends it, not the participant.

## Source evidence

Pinned source d498de6c4638aa71cdb20189d13642b41abab5f1: `Gateway/Services/CoreApiInboundMessageHandler.cs` (every type without a handler is only confirmed with MessageAck; nothing is told to the core), `Domain/ValueObjects/IpsMessageType.cs` (`Camt055`, definitions `camt.055.001.012` and `camt.055.001.08`, `requiresExplicitAck: true`), `Contracts/Camt055/Camt055CancellationRequestDto.cs` (a client-model DTO only; no endpoint or handler uses it), `docs/IPS_Pain001_Camt055_Cancellation_KA.md`. The source has no camt.055 behavior beyond the acknowledgement.

## Design

- `PaymentMessageTypes` gains `Camt055` (`"camt.055"`) and `Camt055Definition` (`"camt.055.001.12"`); the registry spellings `camt.055.001.012` and `camt.055.001.08` are also accepted, as the pain.001 reading accepts `.012`.
- The 007c rule `IsArchivedRecall` is widened to a name that is true for all three (`IsArchivedCancellation`), so `IncomingReceiptPreparation` and `IncomingReceiveWorker` need no other change: after the sequence check the receipt completes as processed, and the worker acknowledges after the commit, never blocking polling.
- No verification of schema or signature, as in 007c, recorded as a limit: anything later built on these messages must verify them first.
- No Contracts change, no migration, no endpoint.

## Acceptance

- A camt.055 receipt (short type, `.12`, `.012`, `.08`) completes as processed, creates no payment, transfer or reply, makes no remote call, and keeps its raw XML.
- A missing or non-positive sequence holds it; a duplicate sequence is processed once.
- The worker acknowledges it after the commit with the sequence header, also on redelivery after a failed acknowledgement; `camt.053` stays held and unacknowledged.
- The 007c recall behavior and every other incoming type are unchanged; the full suite passes with no existing test changed.
- Build, full tests, format, independent Standards and Spec reviews.

## Open question for the owner

Source parity is acknowledge and archive. The gap is that a PISP's cancellation of an initiation we hold is silently archived, so nobody is told to stop or answer it. Closing it needs a Contracts receive method and an owner decision, as for recalls; this slice does not decide it.
