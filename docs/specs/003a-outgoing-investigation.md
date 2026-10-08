# Outgoing reliability: investigation and authorized resend

Status: source-derived reliability roadmap. Owner approved merging b2542e1 and continuing on 2026-10-05; codex/capability-rebuild is now at b2542e1. Active branch: codex/outgoing-investigation. The first focused slice is [003a.1 protocol](003a1-investigation-protocol.md); durable workflow and resending follow separately.

## Evidence

Original reference: d498de6c4638aa71cdb20189d13642b41abab5f1. Read API/Transactions/Pacs008StatusInvestigator.cs, API/Transactions/TransactionRecovery.cs, API/Services/Pacs028XmlMessageBuilder.cs, Application/Options/IpsTransactionOptions.cs and Tests/Api/TransactionRecoveryTests.cs (all project prefixes IPS.MiidleWear). The source tests cover schema/identifier preservation, original rejection, 1016 resending without PossibleDuplicate, 1017 retry and window exhaustion. Source comments cite Annex D sections 3.2.9 and 8.1.7; those citations are not independent verification of the protocol document.

## Business behavior

- Investigate uncertain outgoing pacs.008 payments without repeating the initial submission. Use the existing aggregate, journal, committed payment claim and shared unit of work.
- Build pacs.028.001.06 referring to exact pacs.008.001.12 identifiers and original acceptance timestamp. Persist investigation identity and selected wire XML before sending. Keep signing, schema validation and development-only unsigned rules.
- A trusted, correlated report of the original payment can establish acceptance/rejection. A rejection of the investigation itself cannot establish payment rejection.
- Source 1016 means no transaction found and can authorize a separately journaled resend; 1017 and ambiguous AG09 remain unresolved. Invalid signature, wrong identity/version, malformed content, transport failures and unknown codes cannot authorize resend.
- Resend reuses the original payment identifiers and exact stored payment XML. The source sends pacs.008 after 1016 without PossibleDuplicate. A lost resend response requires investigation again, never automatic repetition of the marked resend.
- Preserve the first final outcome. Save outcome, history, callback obligation, schedule and ownership release atomically. Existing exact-outcome status acknowledgement remains unchanged.

## Durable boundaries

Give each investigation and authorized resend its own immutable journal identity, correlation and submission marker. Save full HTTP response evidence before interpretation, including unsuccessful/malformed responses. Replay saved responses before expiry checks or remote calls. Parent rowversion and committed ownership fence every checkpoint; no database transaction spans I/O. Persist authorization and counters so a restart cannot reuse one authorization for multiple sends. Expired work without conclusive evidence goes to ManualReview and creates its existing callback obligation.

## Source defaults to preserve unless explicitly changed

First investigation delay 9 seconds; subsequent delays 30 seconds, 1 minute, 5 minutes, then 15 minutes. Window 24 hours from original acceptance (source falls back to creation). MaxResends 3; MaxAttempts 0 means window-limited. Source discovery interval 5 seconds. Bind validated options through appsettings; keep current transport, execution and ownership budgets and no automatic HTTP retries/hedging.

## Decisions and evidence still required

1. Source dispatcher schedules the first investigation nine seconds after recording uncertainty, while recovery of stuck sends schedules immediately. Preserve these distinct source paths unless a unified rule is approved; independently verify the protocol deadline before live activation.
2. Resolve MaxAttempts semantics: its documentation says investigations plus resends, but RecoverAsync increments once for the investigation and immediate resend only increments ResendCount. Preserve observed behavior until an explicit change is approved.
3. Specify the exact window boundary (source uses greater-than), remaining-call budget, and whether 1016 allows resending outside the initial 20-second submission window. Confirm the protocol retention/resend conditions before enabling resends.
4. Specify exact correlation for responses about the original payment versus the investigation. Replace the source prefix-based name check with exact supported message definitions under the already approved protocol-validation policy.
5. Confirmed: Gateway/Services/IncomingStatusReportApplier.cs and its tests apply unsolicited pacs.002 reports to outgoing payments and schedule callbacks. Add a dedicated durable incoming-status slice, with signature/correlation validation, receipt handling and protocol acknowledgement evidence. This is still required for full outgoing pacs.008 parity.

## Focused reviews

1. Protocol and durable investigation: schema-valid requests, independently signed response fixtures, journal evidence, deadlines/retries and final outcomes. Until resend is implemented, retain a durable not-found obligation rather than dispatching a payment.
2. Protocol-authorized resend and runtime integration: one-use authorization, durable attempt accounting, supervised SQL discovery and callbacks. No new public endpoint or Contracts change.

## Acceptance

Use real SQL and independent IPS fixtures. Cover original accepted/rejected, 1016, 1017, ambiguous AG09, rejected investigation, wrong signature/correlation/version, lost replies, saved-response replay after expiry, exact retry boundaries, exhaustion, competing claims, stale writes, rollback and cancellation. Kill processes around request/marker/response/interpretation/resend checkpoints. A simulator that processes the payment then loses the response must receive an investigation before any further payment send. Verify XML byte reuse, identifiers, durable counters, one-use authorization and atomic callbacks. Complete build, full tests, formatting, Contracts/architecture and generated-model checks, independent Standards/Spec reviews, and owner approval before merging. No production activation.
