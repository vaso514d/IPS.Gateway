# Review 003a.1: pacs.028 protocol preparation and reply interpretation

Base b2542e1, branch codex/outgoing-investigation. The owner approved merging the outgoing host increment and continuing on 2026-10-05. This is the first focused review under [the reliability roadmap](003a-outgoing-investigation.md).

## Scope and source

Implement callable protocol components only: pacs.028.001.06 XML from frozen validated pacs.008 values, signing using the existing certificate/development policy, and trusted reply interpretation. Source files and tests at d498de6 are listed in the roadmap. Import Tests/Ips/Schemas/pacs.028.001.06.xsd byte-for-byte, preserving the embedded-schema layout. No persistence, transport dispatch, runtime registration, options, migration or Contracts changes in this slice.

## Acceptance

- Request identity is supplied, never generated during building. Preserve original message ID, transaction ID, EndToEndId and acceptance time, including submillisecond precision. Use original participant/creditor agent and frozen protocol profile; UTC output and XSD child order remain explicit. Repeat calls with the same input produce identical XML.
- Use the existing signing policy and independent Java signature verification. Missing certificates fail unless explicitly permitted in development; unsigned disposition must remain accurate. Never sign already signed messages again.
- Share the existing schema/signature/status/correlation verification with ordinary payment replies. Keep the public ordinary-payment interpreter restricted to the exact pacs.008.001.12 definition. The internal investigation path requires exact pacs.028.001.06 plus the investigation message ID; transaction references, if present, must match the original payment.
- A verified final report about the original payment returns OriginalAccepted or OriginalRejected. AG09 or investigation-only 1016/1017 codes cannot establish payment rejection. Pending, contradictory, untrusted, malformed, mismatched and unsuccessful HTTP evidence remains Unresolved.
- A verified rejection about this investigation with AG09 and HTTP request-status RJCT/1016 returns NotFound. RJCT/1017, absent/ambiguous/unknown codes, acceptance of the investigation and other investigation rejections remain Unresolved. NotFound alone performs no send and is not a durable resend authorization.
- Require HTTP 200, a valid signed pacs.002 body and agreeing reported statuses. An HTTP header alone cannot authorize a resend. Preserve the existing payment reply policy; do not broaden its accepted names or bypass signature verification.

## Evidence and later decisions

The source's prefix checks and unchecked report classification are not copied: strict names, trust and correlation follow the approved rebuild policy. Real IPS interoperability remains unverified. Source mock reports may omit EndToEndId on a transaction report; our existing strict payment profile does not accept that omission. Resolve the independent protocol evidence before broadening this boundary. No live investigation is enabled here.

Durable requests/responses, committed ownership, scheduling/counters, not-found obligations, resends, runtime dispatch and unsolicited pacs.002 handling remain subsequent reviews. The roadmap records source timing/counter ambiguities; this slice does not decide them by adding configuration.

Run focused independent protocol fixtures and the complete existing SQL suite, build, formatting, architecture/Contracts, model consistency and host smoke checks. Obtain independent Standards and Spec reviews. Present for owner approval before commit/merge.
