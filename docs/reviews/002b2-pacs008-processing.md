# Stage 2b.2 review: resumable outgoing pacs.008 processing

Branch codex/pacs008-processing; base 74c38db (2b.1 merged into codex/capability-rebuild by owner approval on 2026-10-04). Owner approved committing and merging the reviewed changes into codex/capability-rebuild on 2026-10-04, with the latest SQL verification limitation below recorded.

## Result and diff guide

An accepted pacs.008 can now be processed or resumed by one Application workflow from its committed progress. No endpoint, worker, HTTP transport or Contracts change is introduced.

- Application/Payments/Pacs008/Pacs008Intake.cs: a known reference returns the stored payment before validation; new requests are validated against current policy, then the shared OutgoingTransactionIntake stores request, identifiers and AcceptedPacs008 in one commit. Duplicates stage nothing.
- Application/Payments/Pacs008/Pacs008Processing.cs: ownership, checkpoint resume, deadline, deferred preparation, marker-before-I/O, response-before-interpretation, outcome-with-release. A lost race ends the run and reports the last committed outcome.
- Application/Abstractions/Payments: IPacs008MessagePreparation (SigningResult: SignedMessage or SigningDeferred), IIpsTransport, IIpsReplyInterpreter. IOutgoingPaymentRepository.Add takes an explicit snapshot argument. Application/Payments/PaymentMessageTypes holds the pacs.008 constant.
- Application/Transactions/OutgoingTransactionWork.cs: recovery releases pacs.008 preparation and stored responses; only marker-without-response becomes Uncertain.
- Pacs008ProtocolProfile moved to Application so intake can snapshot it; fixed protocol constants moved into Pacs008Message. ValidatedPacs008 gained a private JSON restore constructor; validation still creates new values only through Validate.
- Domain: RecordProcessingFailure raises bounded PaymentProcessingFailed (`payment.processing-failed`) without a state change.
- Infrastructure: Pacs008Preparation over the existing builder and signer, deferring SigningCertificateException and unreachable keys (CryptographicException); ISigningCertificateSource seam; IpsReplyInterpreter and IpsSignatureVerifier; pacs.002.001.14 XSD copied unchanged (git blob 64850bb); PaymentJson shares camel-case/string-enum artifact JSON and versions the snapshot (format pinned by a test); FindDueAsync includes Sending.
- Generated migration 20261004133342_Pacs008AcceptedSnapshot adds nullable AcceptedJson and CK_Transactions_Accepted (pacs.008 and ISJSON). The interceptor makes the snapshot immutable after intake. Historical migrations unchanged; fresh databases only.

## Tests

- Pacs008ProcessingTests (23 SQL cases, independent simulator and Java-signed replies): accepted and rejected outcomes, final outcome unchanged on repeat, lost reply never resent, transport error/timeout uncertain, restart after each checkpoint with crash-injected rollback and fresh-scope recovery, competing and excluded owners, stale owner cannot store a late response, missing/expired/unavailable certificate retry, deadline NotSent (including after certificate retries), development unsigned gating, cancellation before and after the marker and after a received response, snapshot completeness, mapping isolation and pinned JSON, duplicate (including a body current policy rejects) and invalid intake.
- IpsReplyInterpreterTests (14 cases): Java JSR105-signed fixtures for accepted, settled, group-only and rejected replies; pending, contradictory, header conflict, mismatched message/transaction/end-to-end/original type, untrusted signer, trusted certificate presented for another key, substituted signature algorithm, non-empty reference URI, tampered, unsigned, malformed, schema-invalid, DTD, non-200, header-only and duplicate-header responses.
- Domain and options unit tests. Existing tests adapted only where behavior intentionally changed: certificate exceptions are now SigningCertificateException, and two recovery tests that asserted abandoned Sending becomes Uncertain now use pacs.009, which keeps the conservative rule.

## Verification

- Restore and Release build: zero warnings/errors.
- Full suite: 368 passed, zero failed/skipped (203 unit/architecture/Contracts, 165 integration). One intermediate run had transient 30 s LocalDB connection timeouts; the immediate rerun passed without changes.
- Formatting and whitespace checks pass. The generated migration's BOM/CRLF were normalized by dotnet format, as for 2b.1; content is byte-identical.
- Migration command: `dotnet ef migrations add Pacs008AcceptedSnapshot --project src/IPS.Middleware.Infrastructure --context TransactionDbContext --output-dir Transactions/Migrations` (EF CLI 10.0.12). Pending-model check: no changes.
- Clean source export (tracked and untracked files, no build outputs): tool/package restore, Release build, 366 tests, format and EF model check pass.

## Standards

Independent read-only review raised five findings, all addressed: the generic repository's optional snapshot argument is now explicit and the pacs.008 constant moved to PaymentMessageTypes; the snapshot's JSON shape, which follows Application types through a private JSON restore constructor, is pinned by a format test rather than duplicated in a separate storage model; certificate deferral is an explicit SigningResult instead of an exception; the deferral path checks ownership like the outcome path through one release helper; the ledger checkpoint is updated. Layer references, generated migration, Contracts and TimeProvider use were confirmed clean.

## Spec

Independent read-only review found no path that resends after a marker, records NotSent after a marker or lets a stale owner store a response. Addressed: unreachable private keys now defer instead of escaping; commits after the marker ignore caller cancellation so a received response is kept; known references are returned before validation; the deferral release is checked. Added tests for cancellation after a received response, policy-rejected duplicate bodies and signature substitution. Recorded rather than changed: the 45 s ownership versus 20 s window trade-off, the unsupported-snapshot-version loop (only version 1 exists), byte-identity reply trust, and no SQL interleaving test for recovery racing a marker commit (rowversion fencing is covered by 2b.1 tests).

## Follow-up review

An independent read-only review of the fixes confirmed the signing result, post-marker commits, duplicate-first intake, explicit snapshot argument and pinned JSON. It found that the KeyInfo-swap test's text replacement never matched Java's line-wrapped base64; the test now rewrites the X509Certificate element structurally and asserts the trusted certificate is present. Release build, 368 tests, format and EF checks pass afterwards; the clean-source export passed immediately before this test-only fix.

## Snapshot and reply-correlation fixes — 2026-10-04

The JSON restore constructor now copies geolocation, instrument codes and structured remittance into read-only collections without applying current validation policy. The SQL snapshot test attempts to mutate each restored collection and verifies XML remains unchanged. Reply correlation requires ordinal equality with the emitted pacs.008.001.12 identifier; independently Java-signed fixtures reject pacs.0089, pacs.008-invalid and pacs.008.001.11, while the supported identifier passes.

Latest verification: Release build succeeds without warnings/errors; 203 unit/architecture/Contracts tests and 65 non-SQL integration tests pass. Formatting, whitespace and EF model consistency checks pass. The full suite was attempted but stopped after repeated LocalDB startup failures (SQL error 50, 0x89c5010a); SQL snapshot verification is blocked. Earlier full-suite results above predate these fixes. Independent Standards and Spec rechecks found no remaining actionable findings. No schema or Contracts changes were made for this fix; owner subsequently approved commit and merge with the SQL verification limitation recorded.

## Limits

Investigation (pacs.028), resending and manual review remain the reliability capability; Uncertain payments are left due for it. Certificate-source configuration, HTTP transport, workers, deadlines on callers and callbacks remain Stage 2c. Reply certificate trust is byte identity with supplied certificates; no chain, revocation or validity checks. The verifier supports only the IPS signature profile without xml:* attributes. Pacs.008 rows created through the generic foundation intake have no snapshot and are NotSent (or Uncertain after a marker) if processed.
