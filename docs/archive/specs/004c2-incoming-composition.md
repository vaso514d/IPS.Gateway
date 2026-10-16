# 004c.2 — Incoming workflow composition

Approved owner plan, 2026-10-05. Branch codex/incoming-composition from verified codex/capability-rebuild at 3a3da18. LocalDB recovered and all 674 baseline tests passed before implementation. No commit/merge without owner approval.

## Behavior

A concrete Application coordinator handles one journal ID per invocation. It reads durable receipt routing first, skips held/completed entries, and routes existing replies straight to delivery. Due receipts awaiting payment decisions acquire and commit receipt ownership, then prepare registration in a fresh scope. Registration races retry only the database phase in fresh scopes under the existing committed claim. Attachment, original references, continuation scheduling and receipt release commit together; no receipt claim survives into CBS processing.

Attached payments are reused without repeating protocol registration. CBS runs under its separate committed payment claim in a fresh scope, using existing markers/response replay. A busy or unresolved payment leaves the receipt due after configurable Payments:Incoming:Composition:ContinuationDelay, default one second. The coordinator never spins or repeats marked submissions. Once a decision exists, first-reply scheduling may move forward under the journal rowversion, only for pending receipts without live owners or an existing reply. It cannot override retry/preparation scheduling. The existing reply workflow claims the receipt afresh and performs one durable attempt.

Trusted structural failures save the FF01 envelope and original references and release the receipt in one commit, without a payment or CBS call. Unsupported/untrusted/conflicting/invalid-sequence receipts are held. No MessageAck. Preserve immutable outcomes, exact reply replay, two durable sends 200 ms apart, cancellation evidence preservation, and provisional post-deadline delivery.

## Boundaries

IIncomingWorkflowExecution is the feature-specific fresh-scope execution/notification boundary implemented in Infrastructure. Each scope returns committed immutable data; never reuse failed contexts. IIncomingCompositionRepository supplies routing projection and staged first-reply scheduling using existing tables. Registration retains its existing entry point for prior capabilities and adds an atomic release operation for composition.

Two distinct bounded journal-ID channels share the demonstrated coalescing/FIFO mechanics. Reply notifications occur only after a fresh read sees committed pending reply scheduling. Notification failure/full queue never changes SQL or ownership. Future rediscovery must include pending replies, including those saved before their first send. No generic workflow framework or new schema is introduced.

Composition registration is explicit and test-invoked. Api binds the new setting only; no coordinator activation, workers, admission pools, polling or migrations. Review 3 remains responsible for separate SQL discovery routes and production host wiring. Existing HTTP adapters and Contracts are unchanged.

## Evidence and verification

Source reference remains d498de6: original Gateway/Services/CoreApiInboundMessageHandler.cs and tests Gateway/InboundAckOrderingTests.cs and InboundReadProcessingSplitTests.cs. Existing approved processing/reply specifications govern business and protocol behavior; this slice composes them rather than introducing new payment decisions.

Test the coordinator through real SQL with independently signed XML and HTTP simulators: accepted/rejected immediate replies; fast CBS results; atomic release before remote I/O; FF01/holds; shared canonical payment and distinct receipt references; competing ownership; cancellation/restarts at registration, CBS marker/evidence/decision and reply evidence; stale readiness writes; queue saturation/lost notifications; rollback without publication; exact replay and bounded attempts. Run full suite, build, architecture/Contracts, formatting, host and EF model checks. Independent Standards and Spec reviews required. No production interoperability claim; IPS late-reply retention remains the pre-production verification gate.
