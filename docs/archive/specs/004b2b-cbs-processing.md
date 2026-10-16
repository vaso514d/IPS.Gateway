# 004b.2b — Incoming CBS processing and recovery

Prepared from copy-repository commit 8cd2248 on codex/incoming-processing. Implemented on that branch; verification and independent reviews are recorded in [the review evidence](../reviews/004b2b-cbs-processing.md). Owner approval is still required before merging this slice. The owner approved merging 004b.2a on 2026-10-04; codex/capability-rebuild now contains 8cd2248. Preserve other branches and the read-only original repository. An owner-requested, behavior-preserving cleanup of this slice is specified in [004b2b-cleanup.md](004b2b-cleanup.md).

## Approved behavior and evidence

Source evidence is pinned to d498de6c4638aa71cdb20189d13642b41abab5f1: Gateway/Services/InboundCoreCaller.cs, CoreSystemRequest.cs, CoreSystemClient.cs, InboundCoreReconciliation.cs and CoreReferences.cs; the imported IClientPaymentReceiver and Pacs008PaymentResultDto describe the CBS wire contract. Source paths use the IPS.MiidleWear prefix.

Owner decisions carried forward:

- Keep receiving participant + exact EndToEndId as the internal identity and unchanged EndToEndId as the external CBS idempotency/status reference.
- Require an explicitly supplied ACCP/RJCT status; do not deserialize missing Status into the Contracts DTO's default ACCP. Allow omitted EndToEndId, trusting the associated request/query; a supplied value must match exactly. Supplied original group/transaction identifiers must also match the frozen originating references. Missing optional identifiers do not reject an otherwise valid result.
- Preserve the 20-second IPS window, reserving up to 3 seconds for an inline CBS status check and 2 seconds for reply preparation/delivery.
- Unknown CBS outcome produces RJCT/MS03 and durable reconciliation. If recovery first learns of a CBS credit after the IPS deadline with no stored IPS decision, record the credit separately, decide RJCT/MS03, and retain reversal-required work. Never overwrite an existing IPS decision.
- Successful reversal-request delivery proves acceptance only, not completed reversal. Reversal execution remains a later review.

Compatibility differences must be explicit: missing Status no longer defaults to acceptance; pending/malformed/mismatching results remain unresolved; first submission and each remote result become durable before dependent effects. Public Contracts remain unchanged.

## Workflow and state

Introduce a concrete IncomingPacs008Processing Application workflow by payment ID. It acquires and commits payment ownership before work and resumes from SQL. Use the existing shared unit of work; no transaction spans remote I/O. Keep receipt scheduling/completion separate: this workflow never acknowledges IPS or marks a receipt processed.

Extend IncomingPayment with guarded methods and versioned events for beginning the sole CBS submission, observing an unknown result, recording CBS acceptance/rejection, deciding the IPS result and recording required follow-up. Keep CBS outcome (NotSubmitted, SubmissionStarted, Unknown, Accepted, Rejected), optional immutable IPS decision, and follow-up (None, ReconciliationRequired, ReversalRequired, ManualReviewRequired) separate. Final CBS results retain their original time/details; repeated or contradictory reports append observations, with contradictions requiring manual review. IPS decisions are write-once.

Persist the originating journal ID and its receipt time when the canonical payment is first registered. Freeze DeadlineUtc = (snapshot.AcceptanceDateTime ?? originating receipt.ReceivedAtUtc) + configured 20 seconds in the same registration commit. Duplicates never extend or replace it. Freeze the originating correlation references for interpreting the canonical CBS call; each receipt retains its own existing references for later reply generation. The migration chain remains supported only on fresh databases, so no legacy-row backfill is required.

CBS final outcome data includes reported reference, reason/internal error code, description and processing timestamp. Preserve full raw response evidence. If the optional processing timestamp is absent, use the saved response observation time, never the current clock during replay. Domain remains independent of HTTP, XML, EF and Contracts.

## Durable checkpoints and adapters

Add Application interfaces for CBS submission/status lookup and result interpretation. Submit consumes the stored immutable request and participant context; query uses the same exact EndToEndId. Infrastructure maps/reads the existing Contracts wire shapes without changing them. Implement the JSON interpreter in this slice, but use test adapters for transport; production HttpClient configuration belongs to live integration.

Use an incoming processing repository for tracked aggregate/checkpoint access, alongside the existing payment-work repository. SQL owns immutable call records: operation ID, payment ID, kind (submission/status), owner token, UTC start, and write-once response or failure evidence. Permit only one submission record per payment. A response contains complete HTTP status, body and headers plus observation time. Non-2xx, 404, PDNG, missing/invalid status, malformed JSON or mismatching supplied correlation remain unresolved. Status codes compare case-insensitively as in the source; identifiers compare ordinally. Missing identifiers are allowed, including when the CBS DTO allows them. Missing rejection reason uses MS03 for the IPS decision.

Commit the submission marker and aggregate transition before calling CBS. Recheck remaining budget/ownership immediately after that commit and before dispatch. If interrupted at that boundary, the marker is uncertain evidence even when the call may not have occurred. Never authorize another POST from it. Status GETs may repeat after interruption, but persist any received result before interpreting it. Replay stored unconsumed results before making a new call; do not repeatedly reinterpret old nonfinal responses as new observations.

| Durable state | Next action |
|---|---|
| No submission marker, budget remains | Commit marker, then make the single submission |
| No marker, submission budget exhausted | Decide RJCT/MS03 with CBS NotSubmitted; no reconciliation for a call never authorized |
| Marker without a stored final result | Query status while inline budget remains; never submit again |
| Stored unconsumed response | Interpret that evidence before any further remote call |
| Confirmed CBS outcome, no IPS decision | Record the corresponding decision while the decision window remains; otherwise RJCT/MS03, retaining reversal work for a credit |
| Stored IPS decision | Return it unchanged; preserve any recorded follow-up obligation |

Save outcome, IPS decision, follow-up requirement and ownership release atomically. A final decision leaves normal CBS processing discovery; record a separate durable follow-up due time for the later reconciliation capability. Unknown outcome requires reconciliation; known late credit requires reversal; a known rejection or definitely unsubmitted payment needs neither. A follow-up obligation is persisted business work, not merely a log message.

## Time, cancellation and recovery

Inject TimeProvider. Defaults are 20-second payment window, 3-second inline-status cap, 2-second reply reserve and existing 45-second ownership. Validate positive values, reserved budgets below the payment window, and ownership longer than the maximum configured processing window. Freeze the payment deadline at registration. Recompute budgets at each call rather than reusing an earlier duration.

Submission budget is max(0, deadline - now - 5 seconds). Inline status budget is max(0, min(3 seconds, deadline - now - 2 seconds)); make at most one inline query per run. The workflow need not wait until the deadline after an unresolved inline query: decide RJCT/MS03 and record reconciliation. Never initiate a call with zero budget. At or after the two-second reply cutoff, create no new ACCP decision; retain known credit and reversal work. An already-stored decision is unchanged. Expiry before any marker is provably unsubmitted; expiry after a marker is not.

Use linked per-call cancellation budgets driven by TimeProvider. The workflow cancellation token represents service-owned execution, not an HTTP caller. Service shutdown stops further remote calls and leaves committed evidence for recovery. If a complete response is already available, attempt to save it under still-live ownership using a separate bounded persistence token (default two seconds); failure leaves the marker recoverable. A timed-out/failed call does not establish rejection. Failed units of work are discarded; do not automatically replay external I/O on persistence retries.

Expired owners cannot save responses or outcomes. A new owner may query status; a late result from the old owner is fenced out. In this slice, recovery after the reply cutoff records rejection and the required follow-up without starting background reconciliation. The next review executes that durable obligation. Preserve first follow-up delay of 10 seconds from the decision; status/reversal retry cadence and completion rules remain owned by 004b.2c.

## Verification and review boundary

Use an independent stateful CBS simulator: credit a payment, lose the submission reply, restart processing, then return that same credit through a status query. Assert one submission, no second credit and correct late-rejection/follow-up behavior. Also test explicit ACCP/RJCT, omitted versus mismatched identifiers, omitted Status (including an empty object), pending/404/malformed/non-2xx outcomes, optional timestamps and rejection-reason fallback.

Real SQL tests cover crashes before/after marker, response, interpretation and final-decision commits; atomic rollback, duplicate receipts, competing owners, lease expiry, stale responses, preserved deadlines and immutable artifacts. Inject time to exercise exact budget boundaries and already-expired intake. Test service cancellation separately from call timeout and verify no transaction spans simulated remote I/O. Existing architecture/Contracts and outgoing tests remain required.

Generate migrations with the official EF CLI, preserve historical files, run fresh-chain/model checks, Release build, formatting and the complete suite. Obtain independent Standards/Spec reviews and owner approval before merge. No live CBS/IPS clients, callbacks, reply XML storage/delivery, polling workers, host endpoints or Contracts changes are included. Split the implementation further if needed to keep storage and workflow diffs reviewable.
