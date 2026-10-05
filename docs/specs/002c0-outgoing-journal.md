# Stage 2c.0: explicit outgoing message journal

Owner-approved implementation, 2026-10-05. Branch codex/outgoing-http, base c7c38b9. This first review precedes callbacks and endpoints. User design reference: C:/Users/omo/Downloads/outbound-payment-flow_v4-DETAILED.md. The attached document is design evidence; the conversation's approved adaptations govern implementation.

## Decisions

Keep Contracts, routes and 200/504 policies, OutgoingPayment and its enum/events, and reliable callbacks. Introduce a technical journal without replacing incoming storage. Normal execution will be directly request-triggered and service-owned; a recovery-only channel carries IDs. Workers, endpoints, callbacks and pacs.028 remain following reviews.

Journal states: ReadyToSend, SendStarted, Received, Processed, Failed. Store immutable identity, direction, message definition, wire content, UTC times, parent-message correlation and complete HTTP evidence. A malformed reply still has a response record; its message definition remains unknown until trusted interpretation. Do not equate journal failure with business rejection.

Keep the accepted snapshot, protocol IDs and unsigned preparation checkpoint on the payment. The journal is the authoritative send-ready message and response store; remove SignedXml, SubmissionJson and SubmissionResponseJson from the payment model. The unsigned preparation checkpoint is not a second selected wire-message slot. Development unsigned messages have an explicit disposition, cannot be upgraded/replaced after freezing, and must still satisfy current policy before sending.

One initial outbound pacs.008 and one response per payment in this slice, SQL enforced. Future investigation messages require an explicit extension of this constraint, not fake pacs.028 support now. Response correlation must refer to a message on the same payment. Journal mutations require a committed live payment claim, exact authorized values and parent rowversion fencing. No independent journal ownership or business events for technical transitions. All changes share the existing unit of work.

## Boundaries

1. Existing durable intake/identifiers/unsigned preparation.
2. Persist selected message as ReadyToSend.
3. Commit SendStarted before remote I/O; one original submission only.
4. Commit raw HTTP response as Received before interpretation.
5. Atomically commit Processed (valid acceptance/rejection) or Failed (inconclusive/invalid response), aggregate outcome/events and ownership release.

A valid signed correlated business rejection is Processed. Non-success, malformed, untrusted or mismatched responses never establish a final business result. They retain evidence and leave the payment Uncertain. Transient exceptions/failed saves leave Received recoverable, not Failed. Original outbound rows stay SendStarted after any response.

Resume saved responses before deadlines or sending. Unfinished preparation resumes; ReadyToSend is eligible only within the stored submission window. A marker without response becomes Uncertain without resend. Final/manual-review payments never restart their original submission. No changes to aggregate enums or stable event names.

## Verification

Retain SQL checkpoint, lost reply, cancellation, duplicate intake, competing ownership, stale response and artifact immutability tests. Retarget physical-column tests to journal records. Add complete journal projection checks, successful/rejected/invalid interpretation, atomic interpretation rollback, immutable metadata, development-unsigned recovery and policy changes, direct EF mutation rejection, and SQL uniqueness/correlation constraints. Exercise restart at every durable boundary with fresh SQL scopes and independent IPS fixtures. Run full suite, build, formatting, architecture/Contracts, migration/model and host checks.

Generate OutgoingMessageJournal using the official EF CLI. Preserve historical migrations; the complete chain supports fresh databases only. Drop obsolete columns without data conversion; do not apply to populated databases. Independent Standards and Spec reviews and owner approval are required before merge.

## Source evidence

Pinned source d498de6: API/Transactions/Pacs008TransactionSender.cs, OutgoingTransactionDispatcher.cs and IpsStatusReply.cs as recorded in 002b2; preserve the rebuilt signed/correlated response validation and immutable checkpoint improvements. The new document proposes combined response persistence/processing; owner chose separate evidence commit to preserve crash recovery. Worker recovery also includes pre-send work, not uncertainty alone. The document's new operations API and six-state payment model were explicitly declined.
