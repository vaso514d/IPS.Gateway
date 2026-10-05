# Review 004c.2 — Incoming workflow composition

Branch: codex/incoming-composition. Base: 3a3da18 on codex/capability-rebuild. Owner authorized final cleanup and commit on 2026-10-05; merge approval is pending. The commit containing this checkpoint is the Review 2 implementation checkpoint. Original repository and other branches preserved.

## Delivered

Application IncomingComposition connects owned receipt preparation, canonical CBS processing and immediate first reply through Infrastructure fresh scopes. Registration attachment/references, one-second configurable continuation scheduling and receipt release commit atomically before CBS. Existing replies bypass registration and CBS; attached payments are reused. FF01 freezes an envelope and releases ownership without a payment. Held/completed receipts avoid remote calls.

IncomingCompositionRepository uses existing tables to read routing and stage first-reply readiness under rowversion. It excludes live owners and existing replies, so an immediate first reply never shortens a retry/preparation schedule. The bounded reply channel carries only journal IDs; notification follows committed scheduling and full/lost notifications leave SQL intact. Queue mechanics are shared with the existing processing channel.

No schema migration, Contracts/route changes, workers, discovery split, admission pools or production activation. Explicit composition registration is invoked by tests and reserved for Review 3 host wiring. [Specification](../specs/004c2-incoming-composition.md), [settings](../configuration.md).

Final cleanup separates composition interfaces, options and immutable results into focused feature files and names the reply envelope and scheduling values. Queue deduplication and workflow behavior are unchanged.

## Verification

LocalDB startup succeeded and the complete 3a3da18 baseline passed all 674 tests before implementation, resolving the previous SQL verification blocker.

Final Release build: zero warnings/errors. **695 tests passed, zero failures/skips: 249 unit/architecture/Contracts and 446 integration**, including real SQL Server LocalDB, HTTP/TLS peers and independent Java JSR105 fixtures. The new coordinator suite contributes 19 cases; host settings add two invalid-delay cases and verify an override. JAVA_HOME used C:/Program Files/Java/jdk-20.

Coverage includes accepted/rejected immediate replies, atomic receipt release before CBS, FF01 without payment, untrusted/unsupported/invalid-sequence holds, canonical reuse and distinct references, live ownership, conflicting contents, concurrent readiness writes, restart at registration/decision/reply response, abandoned CBS marker status lookup, saved CBS response after cancellation, failed-registration rollback without publication, full/lost queue notifications, due-time enforcement, exact replay and two-attempt exhaustion. An independent HTTP peer verifies the complete real-adapter path and no MessageAck. Existing workflow tests retain deeper checkpoint and persistence coverage.

Formatting verification and diff whitespace checks pass. Official EF model check reports no changes since the last migration. Host liveness, architecture and Contracts checks pass. No live IPS/CBS service was contacted.

## Standards

Independent Standards review: **zero actionable findings**. Application owns decisions and Infrastructure owns scope lifetimes; the execution interface represents a real scope boundary. Registration retries exclude remote effects. Shared channel mechanics reflect demonstrated duplication. No dependency violations or placeholder workers found.

## Spec

Independent Spec review: **zero findings**. Ownership/registration atomicity, fresh scopes, first-reply fencing, post-commit notification, FF01/hold paths and durable recovery match the approved plan. No scope creep identified.

## Remaining gate

Owner authorized this commit; wait for owner approval before merging this increment into codex/capability-rebuild. Review 3 adds polling, bounded supervised dispatch, reply/retry and CBS follow-up scheduling, separate discovery queries, multi-instance host tests and process-termination tests. Crash coverage here reconstructs scopes and persisted checkpoints; it does not claim process-kill or multi-host verification. IPS late acceptance/retention and actual certificate-store deployment remain the existing pre-production verification questions.
