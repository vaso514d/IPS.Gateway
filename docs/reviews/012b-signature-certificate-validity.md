# Review 012b: IPS signature certificate validity, and the hung-acknowledgement recheck finding

Branch codex/payment-initiation, stacked on 012a (0636b9c) and the unmerged slices before it; no merge is approved. On 2026-10-07 the owner asked to close the audit's certificate-expiry limit before 013 and approved the [specification](../specs/012b-signature-certificate-validity.md) together with the remaining finding of the [recheck](business-flow-audit-recheck-2026-10-07.md). Commit approval is pending. The owner also asked to include the Georgian flow diagrams (`docs/diagrams`, the `.drawio` files, `README.md` and `preview.html`; the nested `.kilo` tool worktree is not part of the repository).

## Delivered

- **Validity at verification (Annex C 2.2).**
  - **Trust and clock together.** `IpsSignatureTrust` carries the configured IPS certificates and the service `TimeProvider` into every IPS verifier. Those are the outgoing reply and pacs.028 interpreters, the incoming pacs.008 reader and reply protocol, the incoming pacs.009, pacs.004 and pain.001 protocols, and the status report protocol.
  - **Order of checks.** `IpsSignatureVerifier` matches the presented certificate, then requires `NotBefore <= at <= NotAfter`, then checks the digest and signature.
  - **Distinct reason.** A matched certificate outside its period gets its own reason, `valid <from> to <to>, <subject>`, with the dates first so they fit the 100-character hold reason. Untrusted signatures keep their existing reasons.
  - **When the check applies.** Incoming messages are judged at their stored receipt time, so every later step reaches the same verdict. A pacs.008 received while the certificate was valid is still answered after it expires. Replies to our sends are judged when they are interpreted.
- **Rotation.**
  - Signature trust certificates load through `CertificateSettings.LoadSignatureTrust`. It accepts one that is not valid yet and still refuses an expired one. TLS and signing loads are unchanged.
  - Readiness logs a not-yet-valid trust certificate once per change, at information level, and leaves the status unchanged. Its description only says that a trust certificate is not valid yet, because 010 keeps certificate detail out of the check results.
- **Recheck finding.**
  - The acknowledgement loop's beat is dated from the oldest acknowledgement still running (`SupervisedBackgroundService.Beat(loop, period, at)`), or now when none is running. One hung acknowledgement therefore reads as a stalled "IPS acknowledgement" even when another slot is free or keeps working.
  - The loop also wakes when an acknowledgement finishes, so a healthy loop's beat stays within one IPS request timeout.

## Tests

- **New:**
  - **Boundaries.** Theories over every verifier family at exactly `NotBefore` and `NotAfter` (trusted) and one tick outside each (refused with the validity reason). For incoming messages the tick is the receipt time.
  - **Real SQL, incoming.**
    - A pacs.008 received outside validity is held with the stored reason, with no payment and no remote call.
    - pacs.009, pacs.004 and pain.001 received outside validity are held, with nothing stored.
    - A status report received outside validity holds the receipt and changes nothing.
  - **Received while valid, processed after expiry.** A pacs.008 is still answered with its pacs.002, and a status report still settles the payment.
  - **Rotation.** An expiring and a next certificate hand over at their bounds, and the refusals carry the validity reason.
  - **Startup.** A not-yet-valid trust certificate loads and is logged while readiness stays Healthy. An expired one fails startup with the validity message. The not-yet-valid notice is logged once across two probes.
  - **Acknowledgements.** With two slots and one hung acknowledgement, readiness reports a stall while the other slot is idle and while it keeps completing acknowledgements, and receiving continues.
- **Proven to fail without the fix:**
  - With the validity check disabled, exactly the 15 outside-validity cases failed.
  - With the readers judging at processing time, the received-while-valid tests failed.
  - With the old heartbeat, both two-slot acknowledgement cases failed.
- **Changed existing tests, none weakened:**
  - **Mechanical.** Verifier construction passes `new IpsSignatureTrust([...], clock)` with the test's existing clock, and incoming port fakes take the receipt time. Files:
    - IncomingComposition, IncomingPacs008Protocol, IncomingReply, IncomingTransfer, IncomingWorkerSql and IncomingStatusReport tests;
    - the Camt029, Camt056, Pacs004, Pacs009 and Pain002 recovery tests;
    - the InvestigationWorkflow, ResendWorkflow, Pacs028Protocol and IpsReplyInterpreter tests, and ProcessingHarness.
  - **ReadinessCheckTests.** `CertificateExpiry` gained a past `NotBeforeUtc`.
  - **Fixture validity.** `IpsReplies.Certificate` now starts one day before the earlier of `validAt` and 2026-01-01, so the fixed October 2026 test clocks and real time both fall inside it. It only moves `NotBefore` earlier.

## Verification

- **Build and checks.** Build 0 warnings; format and imports (IDE0005) clean; no pending EF model changes.
- **Full sequential run** (`dotnet test IPS.Middleware.slnx -c Release --no-build -m:1`) after the review fixes:
  - Aspire 9/9.
  - Integration 1032/1032: 998 before, plus 34 new cases.
  - Unit 480/480.
  - No failures.
- **Receipt-time proof.** Re-run separately after the full run: with the incoming reader and the status report protocol judging at processing time, both received-while-valid tests failed; restored, both pass.

## Independent reviews

**Standards.** No design defects. Fixed:
- **Wrong reason shape.** The new tests asserted the old subject-first reason; they now assert the exact detail, cut to the stored length where it is stored.
- **Acknowledgement beat.** A beat could lag up to two request timeouts while a slot was free; the loop now wakes on completion.
- **Log-once untested.** The log-once readiness notice is now tested over two probes.
- **Smaller fixes:**
  - a cached `Trusted` result, and no null-forgiving operators;
  - a private trust collection;
  - the startup test asserts the validity message;
  - the rotation refusals assert the reason;
  - the oldest-start expression is a named helper, and LINQ is split one operator per line.

Recorded:
- **Duplicated helper.** The `SignatureTrust(sp)` registration helper appears in both the incoming and outgoing registrations; accepted under "concrete before abstraction".
- **Boundary tick.** Readiness counts `NotAfter <= now` as expired, while the verifier still accepts exactly `NotAfter`.

**Spec.** Fixed:
- **The same reason-shape blocker.**
- **Judged at processing time.** Incoming messages were judged when processed rather than when received, so a pacs.008 re-read at the reply step after expiry would have been held with no pacs.002 to IPS although the core might have credited it. Now judged at the receipt time, as Annex C requires.
- **Missing database-level test.** There was no database-level proof for the incoming pacs.008 hold.
- **Docs.** The `docs/configuration.md` reason example now puts the dates first.

Recorded:
- **Readiness naming.** The not-yet-valid certificate is named in the log rather than in the health description, a deviation stated in the spec's implementation notes.
- **Rotation scope.** Rotation is proved through the reply interpreter; all verifiers share the same check.
- **Hold-reason length.** The stored reason keeps the dates but cuts the subject. Holds are not logged elsewhere, so the full detail is not kept.

## Limits

- **No live check.** Verified with generated certificates and simulators, not a real IPS certificate change.
- **No revocation.** Signature certificates are not checked for revocation.
- **Clock skew.** A wrong or skewed clock refuses valid IPS messages near a certificate's bounds, as Annex C implies.
- **Startup with an expired certificate.** Startup still fails while an expired trust certificate is configured, so it must be removed after rotation, as `docs/configuration.md` says.
- **Aspire cleanup untested.** The recheck noted that the Aspire start-failure cleanup (012) has no forced-failure test; that remains.
