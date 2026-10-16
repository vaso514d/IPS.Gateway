# Stage 2b.2: resumable outgoing pacs.008 processing

Review branch: codex/pacs008-processing, base 74c38db (2b.1 merged into codex/capability-rebuild by owner approval on 2026-10-04). Owner supplied this plan on 2026-10-04; merge still requires approval.

## Evidence

Pinned original d498de6c4638aa71cdb20189d13642b41abab5f1:
- IPS.MiidleWear.API/Transactions/Pacs008TransactionSender.cs SendAsync: rebuilds XML from stored request JSON and current IpsStpOptions on every send; AppHdr/CreDt is send time; a missing or unloaded certificate sends unsigned.
- IPS.MiidleWear.API/Transactions/OutgoingTransactionDispatcher.cs ProcessAsync: Dispatch:TimeoutDeadline 20 s, measured from the client AcceptanceDateTime (`now - accepted > deadline`), records Rejected TM01/1015 without sending.
- IPS.MiidleWear.API/Transactions/IpsStatusReply.cs: body status wins over X-MONTRAN-IPS-ReqSts; ACCP/ACTC/ACSC accepted; RJCT rejected with the first StsRsnInf/Rsn code (default NARR), first AddtlInf and the integer after `RJCT/` in the header; other statuses uncertain. No reply correlation, schema validation or signature verification.
- IPS.MiidleWear.Tests/Ips/Schemas/pacs.002.001.14.xsd (git blob 64850bb055c23f52cae97b6736a40e08dcebf4b7), copied unchanged.
- Annex D v1.02 §6.3 (pp. 99–100): a processed send returns HTTP 200 with a pacs.002 body; other codes carry no pacs.002. §8.1.8 (pp. 165–168): OrgnlGrpInfAndSts/OrgnlMsgId, optional GrpSts, optional TxInfAndSts with OrgnlEndToEndId/OrgnlTxId/TxSts.

## Approved differences from the source

| Source | 2b.2 |
|---|---|
| XML rebuilt from current configuration on every attempt | Normalized payment and mapping settings are snapshotted at intake; resume never revalidates against current policy |
| AppHdr/CreDt at send time | Envelope creation time fixed at intake |
| Deadline expiry is Rejected TM01/1015 | NotSent, keeping TM01/1015 details; only before the submission marker |
| Missing/unloaded certificate sends unsigned | Preparation stays pending and retries; unsigned only via the existing Development policy |
| Header-only reply can be final | Only a valid, signed, correlated pacs.002 body is final |
| No reply schema, signature or correlation checks | All required; failures are Uncertain with the response preserved |

## Behavior

Intake: a known client reference returns the stored payment before validation, so retries stay idempotent after policy changes. A new request is validated against current policy, then the existing intake atomically stores the request, generated identifiers and a versioned AcceptedPacs008 snapshot (normalized payment, IPS BIC/service level/remittance method, envelope time, submission deadline = AcceptanceDateTime + 20 s by default). Duplicates return the stored payment without staging anything. No key material is stored.

ProcessAsync(paymentId) acquires and commits ownership (Received → Sending, or an unowned due Sending payment), then resumes from durable progress:

| Durable progress | Action |
|---|---|
| Snapshot only | Build unsigned XML, commit |
| Unsigned XML | Sign with the current certificate, commit; Development unsigned policy reuses unsigned XML without filling SignedXml |
| Signed XML | Reuse unchanged |
| Prepared, no marker | Check deadline, commit marker, call transport |
| Marker, no response | Uncertain; never send again |
| Response | Interpret without sending |
| Final | Return stored outcome unchanged |

Response is committed before interpretation; outcome and ownership release commit together. Payments that cannot be owned return without work. A failed save ends the run; its scope is discarded. Remote and certificate calls happen outside database transactions.

Certificate missing/expired/unusable/unavailable, or its private key unreachable: preparation returns an explicit deferral; record a bounded processing-failure event, keep artifacts, release ownership and schedule retry after PreparationRetryDelay (1 s default). Deadline passed before the marker: NotSent. After the marker, transport errors and cancellation never yield NotSent: errors record Uncertain; cancellation during the call leaves the marker for recovery. Once the marker commits, later commits ignore caller cancellation so a received response and its outcome are not lost.

Recovery: expired pacs.008 ownership in Sending is released with no status change when there is no marker (preparation is repeatable) or when a response is stored (it is interpreted without sending). Only a marker without a stored response, or another in-flight status, becomes Uncertain as before. Discovery includes unowned due Sending. A new owner may interpret a stored response but cannot store a response for the previous owner's marker. A snapshot missing for a pacs.008 (only possible through the generic foundation intake) is NotSent before submission and Uncertain after it.

Reply interpretation: HTTP 200 with a body; safe XML parsing; head.001.001.03 AppHdr and pacs.002.001.14 Document schema validation; exactly one enveloped signature in AppHdr/Sgntr using the generated IPS profile (C14N1.1 SignedInfo, enveloped + C14N1.0 reference, SHA-256, ECDSA-SHA256) whose X509 certificate is byte-identical to a supplied trusted IPS certificate; OrgnlMsgId equals the stored message id, OrgnlMsgNmId is pacs.008, and TxInfAndSts (when present) references the stored transaction id and end-to-end id. GrpSts, TxSts and the header status must agree. Source mappings and details are preserved for valid replies; anything else is Uncertain. Investigation (pacs.028) and resending remain the reliability capability.

## Boundaries

Application owns intake, the workflow, snapshot model and three interfaces: IPacs008MessagePreparation, IIpsTransport and IIpsReplyInterpreter. Infrastructure implements XML, signing, certificate lookup (ISigningCertificateSource seam until 2c configuration), reply verification and storage. Tests use an independent IPS simulator and Java JSR105-signed replies. No endpoints, workers, HTTP transport, callbacks or Contracts changes. Stage 2c defaults (25 s IPS timeout, 35 s budget, 45 s ownership, 30 s HTTP wait, concurrency 8) are unchanged; 45 s is the workflow ownership default here.

Recorded trade-offs: ownership (45 s) outlasts the submission window (20 s), so preparation interrupted by a crash or cancellation is normally NotSent rather than resumed; the plan's defaults are kept and 2c may tune them. An unsupported snapshot version fails each attempt without an observation; only version 1 exists. Reply trust is byte identity with supplied IPS certificates, without chain, revocation or validity checks.
