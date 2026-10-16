# 004b.2b cleanup — incoming feature folders and slimmer processing

Owner-requested on 2026-10-05 on codex/incoming-processing, after commit 0bc010c and before the 004b.2b merge. The cleanup becomes part of the 004b.2b review: the slice approved for merge is the cleaned version. [Behavior specification](004b2b-cbs-processing.md).

## Intent and constraints

The owner found the 004b.2b code too verbose and the Application layer folders overloaded. The goal is a smaller, easier-to-read implementation with each type in the folder of the capability that uses it.

- Behavior-preserving. No behavior or SQL schema change; the EF pending-model check must report no changes and no migration is generated. Existing tests change only in `using`/namespace lines, except the one test helper that constructs IncomingPaymentIntake now passes default options; all 509 must pass.
- Approved difference D1 (owner, 2026-10-05): a CBS reply with a duplicate name nested inside an unmapped field is now Unknown instead of being accepted. Such JSON is malformed; Unknown leads to RJCT/MS03 and reconciliation, never a confirmed credit.
- No new packages. Application keeps its FluentValidation-only rule; Domain keeps Stateless only.
- Abstractions/Payments, Payments/ and Transactions/ are untouched, preserving the earlier owner decision.
- Repository interfaces keep their current members and Stage* seams; interceptor rules, migrations and the model snapshot are unchanged.
- No commit or merge without an explicit owner request.

## Application layout

Inbound ports move next to the feature that uses them. Only truly shared abstractions remain in Abstractions/Persistence. Namespaces follow folders.

```
Application/Inbound/
  Receipts/      InboundReceipt.cs (receipt, status, registration, claim, owned and stored records)
                 InboundReceiptIntake.cs, InboundWork.cs
                 IInboundReceiptRepository.cs, IInboundWorkRepository.cs
  Registration/  IncomingPaymentIntake.cs
                 IncomingRegistration.cs (outcome, result and registered-payment records)
                 IncomingPaymentWork.cs (with IncomingPaymentClaim)
                 IIncomingPaymentRepository.cs, IIncomingPaymentWorkRepository.cs
  Processing/    IncomingPacs008Processing.cs (workflow only)
                 ProcessingBudget.cs
                 CoreCall.cs (call kind, header, response, completion, call record)
                 IncomingProcessingSnapshot.cs (snapshot, context, result)
                 IncomingProcessingOptions.cs
                 IIncomingCoreClient.cs, IIncomingCoreReplyInterpreter.cs, IIncomingProcessingRepository.cs
  Pacs008/       IncomingPacs008.cs: protocol values only
Application/Abstractions/Inbound/   removed
```

IncomingProcessingOptions is registered once by AddInboundFoundations and is a required IncomingPaymentIntake dependency, replacing the optional parameter and its silent default. Defaults are unchanged. The processing workflow stays unregistered until a production IIncomingCoreClient exists in the live-integration review.

Infrastructure moves IncomingProcessingColumns into Persistence/Inbound as IncomingPaymentColumns, and IncomingPaymentJson into Persistence/Inbound. Repository locations are unchanged.

## Code changes

Workflow:

- ProcessingBudget is a pure value built from the frozen deadline and options. Submission(now) is max(0, deadline − now − reply reserve − status budget). Status(now) is max(0, min(status budget, deadline − now − reply reserve)). WithinReplyWindow(now) is now < deadline − reply reserve.
- CallAsync commits the call record, rechecks ownership, observes cancellation, then computes the budget once; a zero budget returns without dispatch. One Dispatch helper chooses submission or status query. The per-call timeout remains a TimeProvider-driven CancellationTokenSource linked to the service token.
- IncomingPayment.HasFinalCoreOutcome replaces repeated Accepted-or-Rejected checks.

Aggregate:

- DecideIps computes `final = withinReplyWindow && HasFinalCoreOutcome`. Acceptance requires final and CBS Accepted. The reason is the non-blank CBS reason for a final rejection, otherwise MS03. The description is the CBS description when final, otherwise the existing no-final-result text.
- RequiredFollowUp is the single follow-up rule, in order: ManualReviewRequired is kept; an accepted IPS decision needs none; CBS Accepted needs reversal; SubmissionStarted or Unknown needs reconciliation; otherwise none. DecideIps and RecordCoreResult both use it.
- Observations after a final outcome move to one helper. The Core* columns stay flat to keep the schema.

Reply interpreter:

- System.Text.Json deserializes into a private reply record with case-insensitive names and AllowDuplicateProperties disabled. Unmapped fields go to case-insensitive extension data, so duplicate top-level names, including case variants, still yield Unknown. Under D1 an exact duplicate inside an unmapped nested object also yields Unknown; nested case variants remain distinct JSON names and are accepted. A JsonException or null root yields Unknown.
- Status remains case-insensitive ACCP/RJCT; supplied identifiers must match ordinally; missing identifiers are allowed.

Persistence:

- Typed helpers on EntityEntry&lt;IncomingPayment&gt;, mirroring PaymentColumns for outgoing payments: HasLiveClaim(claim, now) and SetClaim(token, expiry), which also clears a claim with nulls. They replace the hand-copied token/expiry checks and claim clearing in IncomingProcessingRepository and IncomingPaymentWorkRepository. IsOwnerAsync remains a query of committed state.

## Library decisions

- Used now: built-in System.Text.Json strict deserialization.
- Deferred: Polly or Microsoft.Extensions.Http.Resilience on the live CBS HttpClient. Submission POSTs are never retried (DisableForUnsafeHttpMethods); the per-call budget remains the Application timeout.
- Deferred: the Microsoft.Extensions.Options validation source generator until options bind from configuration, placed outside Application.
- Declined: Stateless for IncomingPayment, whose three independent dimensions do not form one state machine; Wolverine/MassTransit sagas and Temporal/Durable Task as disproportionate for one remote call without a broker.

Research reference: the workflow already follows Brandur's atomic phases and recovery points (https://brandur.org/idempotency-keys) and Microsoft's Idempotent Consumer guidance for side effects outside the transaction (https://learn.microsoft.com/en-us/azure/architecture/patterns/idempotent-consumer).

## Verification and review

- Tests first: a DecideIps truth table and the case-variant duplicate interpreter case pass against current code before refactoring; ProcessingBudget boundary tests are written before extraction.
- Restore and Release build with zero warnings, the full suite on LocalDB, formatting verification, `git diff --check` and the EF pending-model check. Moves use `git mv`.
- Update architecture.md, the 004b.2b review evidence and the ledger checkpoint.
- Independent read-only Standards and Spec reviews check the diff against this document, the behavior specification and the behavior-preserving constraint. Present the diff, tests and any findings; wait for the owner.
