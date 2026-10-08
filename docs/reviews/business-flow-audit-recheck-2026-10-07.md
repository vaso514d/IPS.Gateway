# Business-flow audit recheck — 2026-10-07

## Reviewed state and result

Copy repository `D:\vaso\Running\IPS\IPS.Middleware`, branch `codex/payment-initiation`, HEAD `0636b9c`. Relevant commits: `cc5de68` (Aspire environment and startup cleanup), `0636b9c` (012a audit corrections). The owner's untracked `docs/specs/012b-signature-certificate-validity.md` is a proposal, not implemented code, and was left untouched.

All four original findings are corrected in code. One additional diagnostic edge case remains. This recheck used independent Standards and Spec reviewers and static inspection of code and regression tests. No production code, tests, settings, Git branches or commits were changed; tests/builds were not rerun. Only requested diagram documentation and this follow-up record were created.

The existing 012a review records 480 unit, 998 integration and 9 Aspire tests passing (1,487 total). That is the earlier implementation's reported evidence, not a fresh result from this recheck.

## Original findings

| Finding | Current result | Evidence |
|---|---|---|
| F1: MessageAck blocks polling | Fixed in code. Receipt commit is followed by nonblocking queue admission; an independent bounded acknowledgement loop handles remote calls. Both loops are supervised/drained. | `IncomingReceiveWorker.cs`: `RunAsync`, `ReceiveAsync`, `QueueAcknowledgement`, `AcknowledgeQueuedAsync`; `IncomingAcknowledgementTests`. |
| S1: CBS transfer starvation | Fixed in code. `FollowUpAdmission` retains the next category across sweeps, skips running work and alternates after each admission; the worker retains one selector instance. | `Infrastructure/Inbound/Workers/FollowUpAdmission.cs`; `IncomingWorkerTests`. |
| F2: pacs.004 amount ceiling | Fixed in code. Validator and snapshot share `CurrencyOf` / `OriginalCurrencyOf`; null, empty, whitespace, padded and case-varied currency cannot bypass the same-currency comparison. | `Application/Payments/Pacs004/Validation/Pacs004Validator.cs`; `ValidatedPacs004.cs`; `Pacs004ValidationTests`. |
| S2: failed Aspire startup disposal | Fixed in code. A failure during startup, readiness wait or wrapper creation disposes the constructed application before rethrowing. No dedicated forced-failure cleanup regression test was found. | `tests/IPS.Middleware.AspireTests/Stack.cs`, lines 33–53. |

## Remaining finding — P2: one stuck acknowledgement can be hidden by readiness

**Location:** `src/IPS.Middleware.Infrastructure/Inbound/Workers/IncomingReceiveWorker.cs`, lines 143–162 at reviewed HEAD.

**Scenario:** With the default `AcknowledgementCapacity=2`, one acknowledgement is stuck and the other slot is free. The loop does not enter its capacity-full `Task.WhenAny` wait. Instead it periodically times out waiting for queue input and refreshes the acknowledgement heartbeat. Other acknowledgements completing in the second slot can also conceal the stuck one.

**Effect:** The acknowledgement loop remains healthy in readiness even though one request exceeds its expected deadline. Polling itself continues correctly; this does not reopen F1. The normal HTTP timeout bounds ordinary requests, but the explicit hung-request diagnostic acceptance in 012a is not met when one task remains stuck despite that boundary.

**Requirement:** Specification `012a-audit-corrections.md` says a blocked acknowledgement should report the acknowledgement loop as stalled while the receive loop stays healthy.

**Test gap:** `tests/IPS.Middleware.IntegrationTests/Inbound/IncomingAcknowledgementTests.cs`, line 91, configures capacity one. It covers a saturated loop, not one blocked request with spare capacity.

**Suggested follow-up:** Track outstanding acknowledgement age/progress independently of idle loop heartbeats. Add capacity-two cases with one blocked acknowledgement and (a) no further messages, (b) continuing second-slot completions. Verify that the receive loop stays healthy while acknowledgement readiness becomes unhealthy. No fix was made during this recheck.

Both independent review axes identified this same issue; it is one unique finding, not two.

## Correction to the original audit's provenance

The original report incorrectly called F2 an inherited source defect. The original repository has a second guard, so F2 was a rebuild regression and 012a's correction is accurate.

At pinned original commit `d498de6c4638aa71cdb20189d13642b41abab5f1`:

1. `IPS.MiidleWear.Domain/Payments/Pacs004PaymentReturnInstruction.cs:106` normalizes a blank original currency to null.
2. `IPS.MiidleWear.Domain/Payments/Pacs004PaymentReturnInstructionMapper.cs:72` defaults it to the returned currency and passes the normalized original amount/currency to the transaction.
3. `IPS.MiidleWear.Domain/Iso20022/Pacs004PaymentReturn.cs:115` rejects a larger same-currency return. The guard belongs to `Pacs004ReturnTransaction`, declared inside that file.

The historical audit is retained unchanged; this record corrects its classification and supersedes its four finding statuses.

## Scope limits still separate

Runtime certificate-date validation remains unimplemented; 012b is only a specification. Optional unsigned Proxy sending, Proxy reply-correlation policy, archive-only recalls/cancellations, unsupported message types and real IPS interoperability retain their previously recorded limitations. The four corrections do not imply full business-document coverage or production activation.

## Diagram handoff

See [the Georgian flow diagrams](../diagrams/README.md) and [the combined editable draw.io atlas](../diagrams/IPS_All_Flows_KA.drawio). They describe implemented behavior at `0636b9c`, including the corrected nonblocking ACK and shared SQL recovery, and explicitly mark responsibility boundaries and incomplete business flows.
