# 2a.1: stable pacs.008 identifiers and preparation storage

Base: b18938b, merged into codex/capability-rebuild with owner approval on 2026-10-04.
Review branch: codex/pacs008-preparation. This is the first focused slice of 2a.
The owner-approved plan permits splitting capabilities to keep reviews focused.

## Source evidence

Read the original repository at d498de6c4638aa71cdb20189d13642b41abab5f1:
- API/Services/Pacs008IdentifierGenerator.cs generates separate 32-character hex GUID MsgId and TxId.
- API/Transactions/OutgoingTransactionIntake.cs saves identifiers and the original request atomically and returns the existing transaction for a duplicate clientReference.
- API/Transactions/Pacs008TransactionSender.cs consumes stored identifiers, builds XML, optionally signs, and queues XML archival before transport.
- Domain/Payments/Pacs008PaymentInstructionMapper.cs maps MsgId to both AppHdr/BizMsgIdr and GrpHdr/MsgId, TxId to PmtId/TxId; envelope creation time differs from the request's business times.
- API/Services/Pacs008XmlMessageBuilder.cs emits head.001.001.03 and pacs.008.001.12.
- Tests/Api/IpsV1FieldProfileTests.cs and Tests/Security/XmlDsigC14N11InvariantTests.cs are later protocol evidence.
All paths have the IPS.MiidleWear. project prefix. Relevant source history includes 0202969 (intake/concurrency) and 8b9f38c (request persistence).

The old sender regenerates XML and archives through a queue. The approved rebuild requires immutable durable artifacts before progressing. This internal storage change implements that approved difference; no HTTP behavior changes here.

## Scope and design

1. Existing pacs.008 intake generates distinct MsgId/TxId in Infrastructure when adding a new payment. Save identifiers, request, aggregate, and received event in the same unit of work. Other message types do not acquire pacs.008 preparation metadata.
2. Keep these protocol details out of Domain. Application exposes a read-only preparation snapshot and a payment-specific repository interface; Infrastructure owns the SQL implementation and GUID generation.
3. Store the two identifiers, unsigned XML, and signed XML as nullable Infrastructure metadata on the existing version-checked transaction row. Two fixed artifact slots are sufficient for this capability; no generic document framework or new checkpoint state machine.
4. Writes require a tracked, persisted pacs.008 in Sending with the matching unexpired claim. Staging does not release/extend the claim or commit. The shared unit of work commits the artifact with any accompanying events atomically.
5. Save unsigned XML before signed XML. Reject empty values. The first value in each slot is immutable; an exact ordinal repeat is a no-op, differing content is refused. Preserve Unicode, whitespace and line endings exactly. This repository stores strings, not a new encoding or XML normalization.
6. Identifiers cannot change after intake; existing artifacts cannot be changed or cleared, including through direct EF tracked edits. Artifact additions require repository authorization, not merely an authorized ownership operation. Check SQL rowversion on every actual artifact write.
7. Artifact metadata alone does not change business state, outcome time or event sequence. Event validation continues to require pending events for changes to Domain properties, while permitting Infrastructure shadow-metadata-only writes.
8. Reopening a fresh context returns the committed identifiers/artifacts. A stale writer cannot persist after another owner/recovery/concurrent update. Failed or cancelled commits roll back artifacts and events; failed scopes must be discarded.
9. Additive migration generated with official EF CLI. Preserve historical files; the complete chain is tested on fresh databases only. Previously populated disposable rebuild databases must be recreated; this slice does not backfill identifiers.
10. DI resolves this repository in the same scoped context as existing repositories and IUnitOfWork.

## Acceptance evidence

Real SQL tests cover intake/reopen, competing duplicate intake, identifier uniqueness, other message types, exact artifact round trips, repeated/different writes, ordering, no claim/wrong token/expiry/foreign payment, competing artifact writers, recovery fencing, rollback/cancellation with events, direct EF mutation refusal, and DI composition. Full dependency, Contracts, host, formatting and migration consistency checks remain required.

## Owner-approved validation ownership revision

The owner explicitly removed inline request/cancellation checks from intake and approved validation at Application entry before workflow execution. Preserve their move of repository interfaces to Application/Abstractions/Payments and other cleanup.

ValidatedIntakeRequest has a private constructor and read-only properties. Its Application factory collects field errors, requires nonblank message type/reference/payload, checks existing SQL length limits (16/35), normalizes type/reference once, and preserves payload text. Intake accepts only this type and performs persistence/idempotency decisions without repeating input checks. This foundation envelope does not certify JSON syntax or pacs.008 business fields; those rules belong to the next capability's Application validation before creating this envelope.

Cancellation remains passed to storage and the shared unit of work. Test storage must honor it on lookup and pending saves; do not restore inline cancellation checks to make an inaccurate fake pass. Domain transitions and Infrastructure constraints remain enforced independently.

Tests cover invalid/null/oversized input, collected structured errors, normalization/payload preservation, cancellation, and all existing durability and duplicate behavior. No schema change is needed for this revision.
## Remaining 2a and boundaries

Next 2a.2 specifies and implements request validation, XML generation, XSD validation, signing and preparation through these storage interfaces. Inspect the full validator/profile tests and independent fixtures before implementing. Resolve signing details from source/specification: the source permits unsigned development sends and uses a constrained C14N 1.1 approximation; neither is silently promoted to a new production policy in this storage slice.

No sender, checkpoint orchestration, HTTP payment endpoint, worker, callback, external certificate, or remote connection is added. Stage 2b/2c remain separate reviews. Owner approval is required before merging this slice.