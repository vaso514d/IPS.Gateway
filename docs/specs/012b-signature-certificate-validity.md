# Specification 012b: IPS signature certificate validity at verification time

Status: specification on codex/payment-initiation, stacked on 012a (0636b9c) and the earlier unmerged slices. On 2026-10-07 the owner asked to close the audit's certificate-expiry limit before 013, and approved this specification together with the [recheck](../reviews/business-flow-audit-recheck-2026-10-07.md)'s remaining finding (below); implemented and reviewed ([review evidence](../reviews/012b-signature-certificate-validity.md)), commit approval pending.

## Scope

A signed IPS message is trusted only if the trusted IPS certificate that verifies it is within its validity period at the moment of verification. Today validity is checked once, when the trust list is loaded at startup, so a certificate that expires while the service runs keeps verifying messages until a restart. This slice applies to every place that verifies an IPS signature:
- outgoing replies (`IpsReplyInterpreter`, `Pacs028ReplyInterpreter`);
- incoming pacs.008 (`IncomingPacs008Reader`) and incoming replies;
- incoming pacs.009, pacs.004 and pain.001;
- unsolicited pacs.002 status reports.

Not in scope: revocation (CRL/OCSP) of signature certificates; Proxy signatures (unsigned by the 009 decision); TLS certificates, which the TLS stack already checks per connection; our own signing certificate, which `Pacs008MessageSigner` already checks at every signature.

## Evidence

- Annex C 2.2 (printed page 8): the participant's client library identifies the IPS certificate used for a signature, and "expiry validation is done of the PM certificate so identified" before the signature is checked.
- Annex C 2.1: several certificates can be configured for the same party, "to avoid unavailability issues when certificates expire".
- The source (d498de6c) and specification 002b2 check trust validity only at load. The [audit](../reviews/business-flow-audit-2026-10-07.md) recorded this as an inherited limit.

## Design

- `IpsSignatureVerifier` receives the verification time. The certificate matched by the presented `X509Certificate` bytes must satisfy `NotBefore <= now <= NotAfter`, otherwise the signature is untrusted. Untrusted means each caller's existing outcome for an unverifiable signature: a reply is Unresolved, an incoming message is Held with its reason, and nothing changes state.
- **The clock** is the service's `TimeProvider`, the same one the signer uses for its own certificate. Each verifier gets it where it is constructed today, next to the trusted certificates.
- **The moment.** An incoming message (pacs.008, pacs.009, pacs.004, pain.001, an unsolicited pacs.002) is judged at its receipt time, so every later step that reads the stored receipt reaches the same verdict; a reply to one of our sends (including IPS's answer to our pacs.002) is judged at the moment it is interpreted.
- **Rotation.** A configured trust certificate whose validity has not started yet is accepted at load, so the next IPS certificate can be configured before IPS switches to it; it verifies nothing until its start. A trust certificate that has already expired still fails startup, as now. Readiness already reports expired and soon-expiring certificates (010); it now also names a trust certificate that is not yet valid, as information, without affecting the status.
- **Logging.** A signature rejected because its certificate is outside the validity period gets its own reason ("IPS certificate outside its validity period", with subject and dates), distinct from a bad or unknown signature, so operators can tell an expiry from tampering.

## Recheck finding: one hung acknowledgement hidden from readiness

The 012a acknowledgement loop refreshed its heartbeat whenever it turned. With `AcknowledgementCapacity` 2, one hung acknowledgement left a free slot, so the loop kept turning (idle timeouts, or completions in the other slot) and readiness stayed Healthy, short of 012a's acceptance that a blocked acknowledgement reports the acknowledgement loop as stalled. Correction: the loop's beat is dated from the oldest acknowledgement still running (`SupervisedBackgroundService.Beat(loop, period, at)`), or now when none is running. Acceptance: with capacity 2 and one hung acknowledgement, readiness reports "IPS acknowledgement" stalled both with nothing else to acknowledge and while the other slot keeps completing acknowledgements, and receiving continues.

## Acceptance

- For every verifier listed above, a message signed by a trusted certificate:
  - verifies inside the validity period;
  - is untrusted one tick after `NotAfter` and one tick before `NotBefore`, with the existing per-flow outcome (reply Unresolved, receipt Held with the new reason), and nothing changes state.
- **Rotation.** With an expiring and a future certificate both configured, a message signed by the old one verifies before its expiry and is refused after it; a message signed by the new one is refused before its start and verifies after it.
- **Startup.** A not-yet-valid trust certificate loads, and readiness names it. An expired one still fails startup.
- **Test changes.** The test IPS certificates are valid across the fixed test clocks, a mechanical fixture change listed in the review. No existing assertion is weakened.
- **Verification.** Build, the full suite (`-m:1`), format and imports, the EF check (no model change), and independent Standards and Spec reviews.

## Implementation notes

- The trust list and the clock travel together as `IpsSignatureTrust`, which replaces the certificate collection in every IPS verifier. The verifier checks validity after the presented certificate is matched and before the digest and signature, so a message from a matched certificate outside its period reports the validity reason even if it was also altered.
- Untrusted signatures keep their existing reasons. The validity outcome uses its own reason: incoming messages are held with "IPS certificate outside its validity period: valid <NotBefore> to <NotAfter>, <subject>" (UTC, `yyyy-MM-ddTHH:mm:ssZ`); replies and status reports are unresolved with "The IPS reply was signed by a trusted IPS certificate outside its validity period: ...". The inbound hold reason column is 100 characters, so the dates come first and the stored reason keeps both of them, with the subject cut; a status report hold keeps only the reply prefix. An incoming reply's pacs.008 read keeps its existing generic hold reason.
- Receipt time: `IpsSignatureTrust.Check(xml)` judges at the clock's now, `Check(xml, at)` at a given moment. The incoming ports take the stored `ReceivedAtUtc` (`IIncomingTransferProtocol.Read`, `IIncomingReplyProtocol.Read`, `IStatusReportProtocol.Interpret`, `IncomingPacs008Reader.Read`), so a pacs.008 received while the certificate was valid is still answered after it expires, and the reply step cannot hold a payment the CBS already credited. `IpsReplyInterpreter` keeps judging outgoing replies, investigation answers and IPS's answer to our pacs.002 at now; only `StatusReportProtocol` passes a receipt time.
- Readiness names a not-yet-valid trust certificate in its log (information level, once per change), not in the health check description, because the check's results carry no certificate detail (010). The description only says that a signature trust certificate is not valid yet.
- `CertificateExpiry` carries `NotBeforeUtc`.
- Test change: `IpsReplies.Certificate` defaults to a validity from 31 December 2025 (or one day before an earlier `validAt`) to one year after `validAt` or now, so the fixed test clocks (October 2026) and the real time both fall inside it.

## Risk

A clock that is wrong, or skewed, rejects valid IPS messages near a certificate's start or end. That is the behaviour Annex C requires; real IPS certificates are valid for years and are rotated with overlap.
