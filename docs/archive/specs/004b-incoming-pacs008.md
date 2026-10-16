# Incoming pacs.008: roadmap and active protocol specification

Review branch codex/incoming-pacs008, base 41bb72f. Incoming foundations were merged as a277635. The owner authorized continuation and explicitly approved preserving deadline rejection plus subsequent CBS reconciliation. This document records source evidence and the next processing reviews; it does not yet authorize unresolved behavior changes or live integration.

## Source evidence

All original references below are pinned to d498de6c4638aa71cdb20189d13642b41abab5f1, not the original working tree.

| Source | Existing behavior to characterize |
|---|---|
| IPS.MiidleWear.Gateway/Services/Pacs008InboundPaymentMapper.cs | Core-facing Pacs008InstantPaymentRequestDto; clientReference absent; debtor participant BIC retained; original MsgId/TxId/UETR/AppHdr/settlement references retained separately for reply; single-payment restriction, FF01 for batch/count violations |
| IPS.MiidleWear.Tests/Gateway/Pacs008InboundPaymentMapperTests.cs | MCC/SERV structured references, initiation channel/instruments, exactly one transaction and NbOfTxs checks |
| IPS.MiidleWear.Gateway/Services/CoreReferences.cs and CoreSystemClient.cs | CBS Idempotency-Key and status-query reference are EndToEndId; never replace this external reference with a new internal GUID without approval |
| IPS.MiidleWear.Gateway/Services/InboundCoreCaller.cs | After failed receive call, one inline status GET if time remains; only ACCP/RJCT final; 404 remains unknown; otherwise return RJCT/MS03 while preserving CoreOutcomeUnknown |
| IPS.MiidleWear.Gateway/Options/CoreSystemOptions.cs | Source defaults: CBS timeout 20 s, inline GET cap 3 s, first reconciliation after 10 s, retries 30 s/1 min/5 min then every 15 min, 24 h window, discovery every 5 s |
| IPS.MiidleWear.Gateway/Services/InboundCoreReconciliation.cs and Tests/Gateway/InboundCoreReconciliationTests.cs | After pacs.008 rejection, CBS RJCT/404 settle rejection; CBS ACCP requires rejection notification/reversal request; pending/failure retries until manual review |
| IPS.MiidleWear.Gateway/Services/Pacs002StatusReportBuilder.cs | pacs.002.001.14, correlated original identifiers, generated response identifiers and creation time; old signing was conditional |
| IPS.MiidleWear.Tests/Gateway/InboundAckOrderingTests.cs | pacs.008 has no MessageAck; its pacs.002 follows payment handling |

## Confirmed direction

Use the existing durable journal and ID scheduling. Introduce a separate IncomingPayment aggregate with guarded methods and immutable events alongside current state. Loading it never replays history. Keep technical receipt ownership in the journal; payment state, required artifacts and the journal checkpoint must commit atomically through the existing scoped context/unit of work. No SQL transaction spans a remote call.

Keep internal PaymentId stable and distinct from the existing CBS EndToEndId reference. Different IPS sequences do not prove different payments; business deduplication must precede CBS submission. Source mapping and Contracts JSON remain compatibility requirements. Incoming values must not be run through outgoing-intake policy merely to reuse its validator.

Persist explicit checkpoints: validated payment/core request and original references; CBS submission marker; complete CBS response; interpreted core outcome; IPS response decision; immutable unsigned/signed pacs.002 artifact; pending delivery. A restart after a CBS marker without a result investigates status rather than reposting blindly. Read a stored CBS result instead of repeating its remote call. Generate response identifiers/time once and reuse the stored signed message on response retry.

Maintain separate CBS outcome and IPS reply decision. Owner-approved behavior: if CBS is still unknown at the reply deadline, prepare RJCT/MS03 but retain unknown CBS state and durable reconciliation work. Later ACCP cannot replace the stored IPS rejection with ACCP; record the discrepancy and request CBS reversal through the preserved callback contract. A successful callback does not itself prove reversal unless the CBS contract confirms that meaning.

Use a single final outcome definition (ACCP/RJCT); PDNG, timeout, malformed/unmatched reply and lost connection remain unresolved. Signature verification and schema validation are required before trusting incoming payment data. Outbound response signing follows the explicit Development-only unsigned policy already implemented; no silent unsigned fallback.

## Focused review slices

1. **Protocol preparation:** namespace-aware pacs.008.001.12/head.001.001.03 parsing and validation; IPS signature verification using the established trust profile; immutable internal inbound values/original references; compatibility mapping to the existing CBS DTO; correlated pacs.002.001.14 generation, validation and signing. Reuse concrete XML/signing helpers where they demonstrably fit. Independent signed fixtures and hand-authored expected CBS JSON, not round trips through the production outbound builder, establish correctness.
2. **Durable business processing:** IncomingPayment transitions/events, business identity, immutable prepared request, CBS checkpoints, outcome/reconciliation policy, immutable response storage and pending delivery. Generated EF migrations and real SQL race/crash tests. Interfaces for CBS and response preparation are Application dependencies; simulators exercise them without live hosts.
3. **Live integration (separate capability review):** typed IPS/CBS clients, certificate sources, receive/processing/response-retry pools, status queries/reversal delivery, periodic SQL recovery, bounded concurrency, shutdown and host configuration. No receive loop is enabled before its supported message and acknowledgement behavior is complete.

## Decisions and remaining integration evidence

- Invalid or untrusted input: owner approved on 2026-10-04; policy is to retain diagnostic state with no CBS call and no automatic IPS reply when trust or correlation cannot be established. A trusted, sufficiently correlated single-payment-rule violation retains source FF01 rejection. This needs an explicit disposition model beyond the foundation's invalid-sequence-only Held constraint.
- Owner approved business identity on 2026-10-04: receiving participant plus EndToEndId; retain EndToEndId unchanged as the CBS idempotency/status reference. Reuse identical frozen payment contents; hold conflicting contents for investigation without a second CBS submission. Actual CBS idempotency/status guarantees must still be verified before live integration; do not claim exactly-once credit from journal deduplication.
- Owner approved deadline allocation on 2026-10-04: acceptance time (receipt fallback) + 20 s, reserve up to 3 s for inline CBS status and 2 s for reply preparation/delivery, shorten initial CBS submission to the remaining budget. Already-expired work never starts a new CBS submission.
- Owner approved reversal semantics on 2026-10-04: successful reversal-request HTTP delivery means acceptance only. It does not confirm completed reversal; retain reconciliation/manual-review work until authoritative completion evidence exists. Pin the completion evidence against the actual CBS contract before live integration.

## Required verification

Independent signed valid/tampered/wrong-version/malformed inputs; complete source mapping scenarios and expected JSON; exactly-one-payment restriction; deterministic correlated reply fields and valid signatures. SQL tests for duplicate payment references across sequences, conflicting contents, receipt/payment/event/artifact atomicity, competing execution and stale ownership. Crash injection before/after CBS marker, stored response, outcome and response creation; unknown CBS status/404/late ACCP, immutable rejection plus durable reversal work, no MessageAck for pacs.008, and no regenerated response on retry. Test deadline boundaries with injected time. Run architecture, Contracts, build, format, model/migration and full SQL suites; separate Standards/Spec review and owner approval before merge.

## Active implementation: 004b.1 protocol preparation

Owner authorized continuation after the invalid-message handling proposal on 2026-10-04. This review implements only focused slice 1. Business identity, deadline and reversal decisions were subsequently approved above; slice 1 still adds no workflow placeholders.

Read results are Ready with an immutable Application snapshot, Reject with original references and FF01, or Hold with a diagnostic reason. Require exactly Message/AppHdr/Document, correct namespaces and exact message definition; verify the original signature before trusting correlation. For count/batch violations only, validate each transfer with a corrected count in a temporary copy to distinguish FF01 from other schema failures. Never use that copy for signature verification or business submission. Missing correlation, malformed data, untrusted/unsigned signatures and unsupported versions are Hold. The foundation journal schema is unchanged; these are protocol results, not persisted new journal states yet.

Map original source fields explicitly into internal values and then existing CBS Contracts. Preserve nulls, debtor participant BIC, BILL identification, treasury/IBAN choice, address concatenation, remittance pieces, channel/instrument ordering and original references. Read-only defensive collection snapshots protect geolocation, instruments and remittance. No outgoing validation policy is applied.

pacs.002 replies use caller-supplied stable message/status IDs and timestamp, original group/end-to-end/transaction references and source group/transaction reason layout. Preserve source 35-character additional-information limit, MS03 fallback, default INST service/local instrument and no OrgnlUETR/OrgnlInstrId additions. Builders generate no random IDs and read no clock. Reuse existing signing/profile verification through a reply-specific entrypoint; strict certificate requirement and explicit Development unsigned behavior remain intact. No storage migration, host registration, CBS call, response delivery or Contracts change.
