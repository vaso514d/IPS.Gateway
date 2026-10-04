# Incoming pacs.008 protocol preparation review

Branch codex/incoming-pacs008, base 41bb72f. Owner authorized commit and merge on 2026-10-04. Commit ab6c4f8 was fast-forwarded into codex/capability-rebuild; the review branch is preserved. [Specification, source evidence and remaining workflow decisions](../specs/004b-incoming-pacs008.md). The owner authorized continuation and accepted holding malformed/untrusted/uncorrelated input versus FF01 rejection for trusted, correlated single-payment violations.

## Result and diff guide

- Application/Inbound/Pacs008: immutable incoming payment snapshot with defensive collection copies, original protocol references, explicit Ready/Reject/Hold read results and caller-supplied reply context/decision. Existing internal field records are reused without applying outgoing validation policy.
- Infrastructure/Inbound/Pacs008: namespace-aware trusted reader; source-compatible field mapping; explicit mapping to existing CBS Contracts; deterministic schema-validated pacs.002.001.14 reply construction. No UUID generation or clock read occurs in the reply builder.
- Existing Pacs008MessageSigner now has a reply-specific entrypoint sharing its unchanged cryptographic/certificate policy. No new crypto implementation or silent unsigned fallback.
- Integration tests directly reference Contracts to assert independently expected CBS JSON; the architecture table records this test-only dependency. Production project references and Contracts sources are unchanged.

Malformed/unsigned/untrusted input, incorrect message versions or missing correlation produce Hold. Count-only violations produce FF01 after verifying the original signature and schema-checking each transfer at its original position in a temporary count-corrected document. Other malformed content remains Hold. No normalized XML is submitted or used for signature trust.

## Verification

Final Release build succeeds with zero warnings/errors. All 416 tests pass: 208 unit/architecture/Contracts and 208 integration, including real LocalDB, outgoing workflow recovery, migration-chain recreation and host liveness. Formatting verification, whitespace and pending-model checks pass; this slice has no schema changes or migration.

New coverage: one snapshot defensive-copy unit test and 21 incoming protocol cases. Hand-authored XML and expected CBS JSON cover original IDs/dates, both party kinds, BILL identifiers, indirect participants, IBAN/treasury accounts, ultimate parties, address/remittance concatenation, MCC/SERV references, geolocation and channel/instrument deduplication. Optional fields and source reply defaults/35-character description/seven-digit UTC formatting are pinned.

Java JSR105 signs the incoming fixtures independently and verifies generated replies. Negative cases cover trusted count/batch failures, bad correlation, amounts, versions, namespace, duplicate headers, malformed transaction order and supplementary data; unsigned, untrusted, tampered and DTD-containing input stays held. Reply tests verify ACCP/RJCT correlation, stable unsigned output, source reason layout, strict certificate requirement and explicit Development-only unsigned mode. Signed XML itself need not be byte-deterministic; the later workflow must persist it once and reuse it.

## Standards

Independent read-only review: zero documented violations and zero actionable heuristic smells. Responsibilities remain within layers, concrete signing reuse has demonstrated callers, and protocol tests use independent evidence.

## Spec

Independent review identified a count-normalization ordering defect. The fix validates each transfer in its original position in a separate temporary document, preserving supplementary-data ordering without repairing malformed XML. Independently signed regression cases cover valid supplementary count violations (FF01) and malformed first/later transfer ordering (Hold). The reviewer rechecked the fix and reported zero remaining findings.

## Owner-requested cleanup — 2026-10-04

Behavior-preserving, no test changed. The reader matches the envelope with one list pattern, returns through a Hold helper, and validates count violations with one fresh single-payment copy per transfer. Mapping owns its namespaces instead of importing them from the reader, shares one debtor/creditor party extraction and the transaction-over-group payment type, and builds original references with named arguments. The reply builder lays out header and report blocks in schema order. The signer's reply entrypoint and the snapshot's defensive copies were tidied. Afterwards: Release build zero warnings/errors, 416 tests pass, format, whitespace and EF model checks pass.

## Boundaries and next step

This is protocol preparation only. Hold/Reject are return values, not new persisted journal states. There are no CBS calls, IncomingPayment aggregate, persistence changes, acknowledgements, response delivery, polling or hosted workers. Supplied trusted IPS certificates use the existing byte-identity/profile verification; chain/revocation policy and certificate configuration are not expanded.

The owner approved the payment identity/conflict rule, reserved deadline budgets and acceptance-only reversal semantics on 2026-10-04. Prepare the next durable-processing specification on a stacked branch; owner subsequently approved the merge, now completed into codex/capability-rebuild. The approved unknown-CBS policy remains RJCT/MS03 at the IPS deadline plus durable reconciliation, with CBS uncertainty kept separate from the reply decision.

Cleanup recheck: independent Standards and Spec reviewers both reported zero findings against the live owner edits. The complete verification above was rerun successfully before the authorized commit.
