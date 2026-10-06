# Review 005a: outgoing pacs.009 and the shared outgoing core

Branch codex/outgoing-pacs009, base 14bc5a4. On 2026-10-06 the owner approved the slicing and decisions, then the [specification](../specs/005a-outgoing-pacs009.md); commit and merge approval are pending.

## Delivered

**Shared outgoing core (pacs.008 behavior unchanged).**
- `Pacs008Processing` became `OutgoingPaymentProcessing`, which handles any supported outgoing message type. Each type supplies only an `IOutgoingMessageProtocol` (build and sign XML) and an `IAcceptedPayment` snapshot (end-to-end id, optional pre-send deadline, caller-supplied protocol ids).
- The pacs.008-only gates are now "supported outgoing type": checkpoint ownership (`OwnedOutgoing`), the submission read, protocol-id assignment, the CBS status outbox, resumability after a crash, and the host runtime routing. `PaymentMessageTypes` names the supported set and the one pacs.008-only rule (`HasInvestigation`).
- The accepted snapshot JSON, the journal message definition and the reply interpreter's expected definition now follow the payment's message type; stored pacs.008 snapshots read back unchanged.
- `ResendExchange` is the single implementation of the steps both resend workflows share.

**pacs.009.**
- `Pacs009Request`, a validator that ports every source rule (ids, agents, currency, amount, priorities, time window, category purpose, purpose code or proprietary text, IBAN structure and checksum), `ValidatedPacs009`, `AcceptedPacs009`, and `Pacs009Intake`.
- The caller chooses the message and transaction ids. Intake rejects an id another payment uses, and the unique indexes fence a race.
- The XML is `pacs.009.001.11` in the IPS v1 profile: financial institutions by BICFI, IBAN-only accounts, no UETR, instruction priority, clearing-system member id or other fields. It is validated against the embedded XSD and signed like pacs.008.
- `POST /api/ips/pacs009/send` returns 200 with the final status, or 504 with the current status after the wait; a duplicate client reference returns the current status at once. The CBS callback and status read work as for pacs.008.
- There is no 20-second pre-send deadline, as in the source.

**Recovery by possible-duplicate resend (`OutgoingDuplicateResend`).**
- After an unknown outcome the exact original bytes are sent again with `X-MONTRAN-RTP-PossibleDuplicate: true` (new `IIpsTransport.ResendAsync`). The first send never carries the header.
- Each attempt is one-shot under its committed marker. A lost reply schedules a new attempt on the 30 s, 1 min, 5 min, then 15 min backoff, inside a 24-hour window frozen on the first attempt. `MaxCycles` caps attempts, and 0 means window-limited.
- A saved reply is interpreted before any deadline check. An exhausted window or attempt limit goes to ManualReview with a callback, and a refused signing disposition ends there too once the window is over.
- The runtime routes by message type and discovers due attempts in the same sweep as investigations.
- An unsolicited pacs.002 about a pacs.009 settles it, verified against the pacs.009 definition.

**Schema.** One additive migration (`OutgoingPacs009`, generated with the EF CLI) widens the type constraints to pacs.009, makes `OutgoingResends.InvestigationId` optional, and adds `DeadlineUtc`, with SQL requiring exactly one of the two.

## Changed existing tests

- Mechanical updates for the renames and new members: `OutgoingPaymentProcessing` and `IOutgoingMessageProtocol` in `ProcessingHarness` and the process probe; `ResendAsync` on the transport test doubles; `IOutgoingPaymentRepository` and `IOutgoingExecution` doubles; request-typed `OutgoingSubmission` tests; `PaymentIntakeResult`.
- Tests that used `pacs.009` as a stand-in for an unsupported message type now use `camt.056` (`AggregateOwnershipTests`, `PaymentPreparationTests`, `PaymentSubmissionTests`): pacs.009 is now a supported, resumable, id-bearing type.
- `OutgoingBindingTests` expects the fourth route and its operation name.
- The pacs.008 snapshot test casts the restored snapshot to `AcceptedPacs008`; the IPS simulator and the host fixture answer for the message definition they received.
- No assertion about pacs.008 behavior was weakened.

## New tests

- **Validation (unit):** every rule above, normalization, caller-supplied transaction id, purpose code versus proprietary, indirect payer.
- **Processing (real SQL, Java-signed fixtures):**
  - the IPS v1 wire profile and schema validity;
  - independent signature verification;
  - rejected and unknown replies;
  - no pre-send deadline;
  - validation errors and idempotency;
  - reused and racing ids;
  - crashes before the unsigned XML, the ready message, the marker and the outcome, each resuming with the committed bytes and one send.
- **Recovery:** first delay; byte-identical flagged resend; backoff through five attempts; attempt cap; exact window end; the clamped last call; abandoned marker; saved reply after the window; competing and stale owners; a refused disposition; discovery excluding pacs.008.
- **Host and transport:** 200 with callback, 400 validation, 504 with immediate duplicate status, flagged recovery of a lost reply over HTTP, and the header only on resends.
- **Process kills:** a real runtime process is killed at the ready message, marker, response and outcome of a pacs.009, and at a resend's ready message and response; IPS receives exactly one message in total, byte-identical to the stored original, with the flag only when the original never left.
- **Status reports:** an unsolicited pacs.002 about a pacs.009, including a wrong-definition report held.

## Independent reviews

**Standards.** No blocking findings. Fixed: the near-duplicate send, interpret, abandon and deadline code in the two resend workflows is now `ResendExchange`; the pacs.008-only rule has one home (`HasInvestigation`); the recovery wording is two whole sentences chosen by that rule; the stale comments, a stray using and the cast invariant comment. Recorded as known naming debt rather than renamed here: `Pacs008Policy`, `Pacs008Options`, `Pacs008Schema`, `Pacs008MessageSigner` and the `Payments/Pacs008` schema folder now serve every type, and the duplicate resend asks `IInvestigationProtocol.MaySend` about the signing policy.

**Spec.** No blocking divergence from the source and no pacs.008 behavior change. Fixed:
- the IBAN rule now includes the structure check, as in pacs.008 and the source, so the schema cannot reject an accepted payment;
- a refused disposition checks the window first, so it cannot retry forever;
- missing acceptance tests (process kills, signed and ready checkpoints, id race, 504 and duplicate status, stale owner, window clamp, backoff, signature verification);
- the spec text for `ResendAsync` and the unchanged ordering;
- this record.

Not changed: a ClientReference already used by another message type returns that payment, as for pacs.008; the public `OutgoingTransactionIntake.AcceptAsync(request)` overload is used only by tests.

## Verification

The last full run, on a machine running nothing else:

- `dotnet build` (Release): 0 warnings, 0 errors.
- **999 tests pass:** 304 unit/architecture/Contracts and 695 integration. The baseline was 925; 74 tests were added.
- `dotnet format` and the IDE0005 unused-usings check: clean.
- `dotnet ef migrations has-pending-model-changes`: no changes; the one new migration was generated with the EF CLI.

An earlier full run of the first version of the change had 9 failures, all expected consequences of pacs.009 becoming a supported type (see "Changed existing tests"); none was a pacs.008 regression.

## Remaining

- **No real IPS verification.** The possible-duplicate header, its idempotent answer and the pacs.009 v1 profile come only from the source and its tests.
- **Not covered by a process-kill test:** a kill after the resend marker (it would need a 30 s backoff wait; the in-process abandoned-marker test covers it) and after authorization only.
- **Naming debt** as above; **Pacs008Options** is still the shared ownership and budget options type.
- **Carried open items:** the three test-only production APIs, and the unexplained intermittent `Pacs008ProcessingTests ("unsigned")` failure.
