# Business-flow audit — 2026-10-07

## Scope and conclusion

Repository: `D:\vaso\Running\IPS\IPS.Middleware`.

Reviewed changes since `077470b` through `651c1ab` on `codex/payment-initiation`, including the uncommitted Aspire/test-environment changes present during the audit. Original behavior reference: `D:\vaso\Running\IPS\IPS.MiidleWear`, commit `d498de6c4638aa71cdb20189d13642b41abab5f1`. Requirements sources: the approved capability specifications and `docs/business-docs`, particularly Annex D v1.02, Annex E v1.01 and Annex C v1.00.

The implementation closely follows the original repository's implemented flows and the approved rebuild decisions. It does not yet implement every flow in the business documents. The audit identified three issues in production paths and one in the test environment. Approved exclusions and inherited limitations are listed separately below.

This was a read-only static audit with independent Standards and Spec reviewers. No builds, tests, database operations or changes were performed during the audit. Existing test reports are historical evidence, not fresh verification of the reviewed working tree. This document was subsequently created at the owner's request; it does not implement or authorize any fixes. Line numbers refer to the audited tree and may move after edits.

## Standards findings

### S1 — P2: CBS follow-up scheduling can starve transfers

**Location:** `src/IPS.Middleware.Infrastructure/Inbound/Workers/IncomingFollowUpWorker.cs`, lines 45–55. `IncomingWorkerOptions.cs`, line 16, permits `CbsFollowUpCapacity=1`.

**Scenario:** Both pacs.008 reconciliation and transfer work are due, capacity is one, and reconciliation has a sustained backlog. Every discovery sweep first admits `payments[index]`. That fills the available slot, so the subsequent attempt to admit a transfer fails and the loop ends. The next sweep again starts with a payment.

**Impact:** Due pacs.009, pacs.004 and pain.001 work can remain pending indefinitely and pass their reconciliation deadlines without reaching CBS. Alternating within a sweep does not provide fairness across sweeps.

**Requirement:** Specification `005c-incoming-pacs009.md`, acceptance scenarios for delivery, recovery and end-to-end settlement, also shared by the later transfer capabilities.

**Suggested correction:** Retain the next category across sweeps or select fairly from both categories. Preserve capacity limits, independent scopes and SQL ownership.

**Verification to add:** With capacity one and a continuously replenished reconciliation backlog, prove that transfer work is admitted within a bounded number of sweeps. Also cover a larger capacity with only one slot becoming free at a time.

**Classification:** Concrete scheduling defect introduced by the expanded follow-up scheduler, not a style preference.

### S2 — P2, test-only: Failed Aspire startup leaves resources undisposed

**Location:** `tests/IPS.Middleware.AspireTests/Stack.cs`, lines 33–42.

**Scenario:** The distributed application has been built and partially started, but startup or a readiness wait throws or reaches its five-minute timeout. `StartAsync` exits before returning the disposable `Stack`.

**Impact:** The caller's `await using` never receives ownership. The partially started application is not explicitly disposed, so resources and shutdown cleanup for temporary certificates/output can remain outstanding until process teardown.

**Requirement:** Resource ownership and reproducible test-environment cleanup documented by specification `012-aspire-test-environment.md`.

**Suggested correction:** Dispose the constructed application if startup, readiness or final wrapper creation fails, then rethrow the original failure.

**Verification to add:** Force a readiness failure after partial startup and verify disposal and cleanup occur even though no `Stack` is returned.

No additional confirmed Standards violations warranted reporting. The approved 006 rewrite explicitly allows removing save interceptors. The examined unit of work retains atomic saves, outgoing parent-before-evidence/event ordering and failed-scope rejection.

## Spec findings

### F1 — P1: A slow MessageAck blocks incoming polling

**Location:** `src/IPS.Middleware.Infrastructure/Inbound/Workers/IncomingReceiveWorker.cs`, lines 49–51 and 95. Timeout selection: `Inbound/Transport/IncomingHttpRegistration.cs`, line 85; configured incoming IPS request timeout: `src/IPS.Middleware.Api/appsettings.json`, line 197.

**Scenario:** The single receive loop saves a receipt and then awaits `AcknowledgeAsync` before its next GET. A delayed or lost acknowledgement can consume the configured 20-second timeout. During that wait this instance makes no new receive call.

**Impact:** Unrelated incoming payments are delayed. Annex D section 2.2, page 18, describes a receive interval of at most five seconds for participant online status; a prolonged gap can cause IPS to mark the participant offline and reject incoming payments. Another healthy instance may mask the gap, but does not remove this instance's defect.

**Requirements:** `003a4-incoming-status-reports.md` line 23, `005c-incoming-pacs009.md` line 32, `007c-incoming-recalls.md` line 21 and `008c-incoming-camt055.md` line 16 require acknowledgements not to block polling. Annex D pages 18–19 requires MessageAck for ISO20022 messages except pacs.008, whose pacs.002 response confirms receipt.

**Original behavior:** At the pinned source commit, `IPS.MiidleWear.Application/BackgroundServices/IpsInboundReceiverService.cs` receives and enqueues at lines 206–222. A separate processing loop at lines 245–267 performs handling, including acknowledgement at line 331.

**Suggested correction:** Dispatch supervised acknowledgement work outside the receive loop while retaining commit-before-acknowledgement ordering, bounded work and shutdown tracking. Do not introduce untracked fire-and-forget calls or MessageAck for pacs.008.

**Verification to add:** Delay an acknowledgement beyond five seconds and prove that subsequent receive calls continue. Retain tests for failed acknowledgements, redelivery, duplicate receipts and shutdown.

**Classification:** Regression against the original polling split and the approved rebuild specifications.

### F2 — P2: Blank original currency bypasses the return-amount ceiling

**Location:** `src/IPS.Middleware.Application/Payments/Pacs004/Validation/Pacs004Validator.cs`, lines 47–50; normalization in `Payments/Pacs004/ValidatedPacs004.cs`, lines 56–57.

**Example:** Supply otherwise valid input with return `Amount=200`, `Currency="GEL"`, `Original.Amount=100`, and `Original.Currency=""` or whitespace.

**Cause:** Optional blank currency passes validation. The ceiling rule compares raw currencies and skips the amount comparison. Normalization subsequently defaults the blank original currency to GEL. The result is a 200 GEL return against a stated 100 GEL original.

**Requirement:** `005b-outgoing-pacs004.md`, line 21, requires checking that the amount does not exceed the stated original amount.

**Suggested correction:** Use the same trimming/defaulting semantics when deciding whether currencies match as when producing the normalized snapshot.

**Verification to add:** Reject excessive returns with null, empty, whitespace and equivalent trimmed/case-varied original currency. Preserve the explicitly intended behavior for genuinely different currencies.

**Classification:** Inherited defect. The original repository has the same raw comparison followed by blank-to-null/default normalization. Preserving source behavior does not establish correctness here.

## Business-flow coverage

| Area | Assessment |
|---|---|
| Outgoing pacs.008 | Main flow, durable evidence, investigation, authorized resend, unsolicited status reports and callbacks are implemented. |
| Incoming pacs.008 | Durable registration, CBS decision, pacs.002 reply, unknown-outcome reconciliation and a single reversal request followed by manual review align with approved behavior. |
| pacs.009 / pacs.004 | Both directions implemented, subject to the scheduling and return-validation findings. Non-pacs.008 possible-duplicate recovery is supported by Annex D page 94. |
| Recalls and cancellations | Partial business coverage: incoming camt.056/029/055 are acknowledged and archived; CBS is not notified and cancellation is not acted upon. This was explicitly approved. |
| Payment initiation | Incoming CBS handoff/recovery and outgoing pain.002 refusal exist. CBS remains responsible for timely PSP-prefixed pacs.008 acceptance or pain.002 refusal and linking the initiation to the resulting payment. |
| Proxy management | Register, update and remove exist. Lookup, verification and notification handling remain excluded. |
| Operations | Recovery, shutdown and diagnostics exist. Measured performance and real IPS interoperability remain unverified. |

## Approved or inherited limits — separate from the four findings

- **Certificate expiry during verification:** Trusted IPS certificates are checked when loaded, but `IpsSignatureVerifier` does not recheck validity dates for later messages. A certificate can expire while the process remains running. Readiness reporting does not itself prevent workers from using it. Annex C section 2.2, printed page 8 (PDF page 9), describes expiry validation during signature verification. The earlier specification `002b2-pacs008-processing.md` explicitly records the narrower trust policy; this is an inherited limitation, not a newly introduced cleanup regression.
- **Proxy security and correlation:** Specification 009 explicitly permits unsigned sending when no certificate is configured and preserves first-item fallback when the expected operation ID is absent. `ProxyProtocol.cs` lines 29–33 and `ProxyReplyReader.cs` lines 41–50 implement those decisions. Annex E sections 2.2, 2.3.1 and 2.3.5 describe signed messages and original bulk/operation references. Current behavior therefore needs separate business/security acceptance before claiming full document compliance.
- **Recall business completion:** Incoming recall/cancellation notifications do not reach CBS; recall refusals are not correlated to finish a tracked recall. Outgoing camt.029 implements a negative camt.056 answer, not the positive/negative camt.055 cancellation answer described by Annex D section 3.2.6. Recall reason lists, maximum ages and indirect-participant recall support also remain limited by approved source-parity decisions.
- **Other incoming message types:** pain.002, pain.013, pain.014 and camt.053 remain unsupported, held and unacknowledged. Annex D's acknowledgement requirement is broader than the implemented subset. These exclusions must not be mistaken for full protocol coverage.
- **Test-environment coverage:** The new Aspire stack does not yet simulate the incoming receive/CBS/acknowledgement path. Existing signing tests use an independent Java verifier, while the Aspire simulator signs with the service's own signer. Passing simulator tests alone does not prove real IPS compatibility.

## Evidence references

Paths below are relative to the rebuilt repository unless the original repository is named explicitly.

- `docs/business-docs/2. GE_IPS_Inception_Report_Annex_D_IPS-Participant_Interface_v.1.02.pdf`: receive/acknowledgement, message flows, possible-duplicate recovery and cancellation behavior.
- `docs/business-docs/3. GE_IPS_Inception_Report_Annex_E_Proxy-Participant_Interface_v.1.01.pdf`: proxy signing, request/response identities and operations.
- `docs/business-docs/GE_IPS_Inception_Report_Annex_C_Security_v.1.00 1_For Participants.pdf`: signature and certificate validation.
- `docs/specs/003a4-incoming-status-reports.md`, `005b-outgoing-pacs004.md`, `005c-incoming-pacs009.md`, `007c-incoming-recalls.md`, `008c-incoming-camt055.md`, `009-proxy-management.md`, `012-aspire-test-environment.md`.
- Original repository at `d498de6c4638aa71cdb20189d13642b41abab5f1`: receive/processing split and pacs.004 validator, value normalization and mapping.

**Review result:** Standards axis: two findings, principally scheduling fairness. Spec axis: two findings, principally acknowledgement blocking polling. No fixes have been applied or freshly tested by this audit.
