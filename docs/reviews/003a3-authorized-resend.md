# Review 003a.3: authorized resend and investigation runtime

Branch codex/outgoing-resend, base 78d1c6b. On 2026-10-06 the owner approved the recommended decisions and asked for implementation. The owner approved committing and merging on 2026-10-06; the commit containing this record was fast-forwarded into codex/capability-rebuild and the review branch is preserved. [Specification](../specs/003a3-authorized-resend.md).

## Delivered

**Investigation.** A trusted 1016 NotFound result moves the payment to Resending and stages a resend authorization, in the same commit as the result. Once `MaxResends` is used up (default 3; 0 disables), the payment goes to ManualReview with the source description and a callback obligation. `MaxCycles` still counts investigations only.

**OutgoingResend:**
- sends the exact stored original pacs.008 bytes and disposition;
- commits the marker before I/O;
- saves the complete response;
- interprets it with the original correlation.

Its outcomes:
- accepted or rejected becomes final, with source Investigation;
- an unresolved reply or a transport failure becomes Uncertain, next investigation by the cycle schedule, capped at the frozen deadline;
- an abandoned marker becomes Uncertain (Recovery), due now, and is never repeated;
- at the deadline, the payment goes to ManualReview, with a saved response always replayed first;
- a development-unsigned original follows the current signing policy.

**Recovery and runtime.**
- Recovery always releases an expired Resending claim, so every resend row gets a result before the payment leaves Resending.
- OutgoingRuntime routes recovered work by status to Pacs008Processing, OutgoingInvestigation or OutgoingResend.
- A separate sweep starts due investigations and resends.
- Investigations and resends use their own attempt budget, and the host shutdown timeout covers the investigation persistence budget.

**Removals and clean-up.**
- InvestigationExecution is deleted, and the NotFound exclusion is removed from discovery.
- Initial-record queries share one `OutgoingJournal.IsInitial` predicate.
- Response consumption is shared through `OutgoingJournal.Consume`.
- The pacs.002 definition is named once, in PaymentMessageTypes.

## Schema

Generated with the EF CLI:

`dotnet ef migrations add OutgoingResends --project src/IPS.Middleware.Infrastructure --context TransactionDbContext --output-dir Transactions/Migrations --configuration Release`

The generated files were then whitespace-normalized with `dotnet format`. Migration 20261005232300_OutgoingResends:
- adds the OutgoingResends table, with unique `InvestigationId` (one-use authorization) and unique (`PaymentId`, `Number`);
- adds `OutgoingMessages.ResendId` with a composite foreign key;
- adds a filtered unique (`ResendId`, `Direction`) index;
- narrows the initial-record index;
- extends the lifecycle check constraint.

Only fresh databases are supported.

## Changed existing tests

- **InvestigationWorkflowTests.**
  - The 1016 case now expects Resending, as the spec requires. The theory was renamed because the investigation itself still sends no payment.
  - `Explicit_registration_runs_recovery_and_processing_in_independent_scopes` was replaced by a registration test for both workflows, since InvestigationExecution was removed.
  - The harness passes the resend repository.
- **OutgoingCrashTests** counts pacs.008 submissions only, because the runtime now also sends pacs.028. Probe setup is shared.
- **OutgoingHostTests** passes `InvestigationOptions` to the runtime constructor and adds the `MaxResends` = -1 startup case.
- **Shared fixtures.** The probe registers the investigation workflows. The host fixture answers pacs.028. The pacs.028 reply builder is now shared in IpsReplies instead of copied three times.

## New tests

**ResendWorkflowTests (18).** These run on real SQL with Java-signed IPS fixtures. They cover:
- an exact single resend, with the original evidence left unchanged;
- a rejected resend;
- `MaxCycles` versus resends;
- `MaxResends` at 0 and 3;
- a lost resend reply resolved by pacs.028;
- one-use authorization in SQL;
- a competing or stale owner;
- an abandoned marker;
- crashes before commit at authorization, ready, marker, response and outcome;
- the exact deadline;
- replay after the deadline;
- a call clamped to the remaining window;
- development-unsigned originals.

**Process kill tests (3).** A real runtime process is killed after:
- the resend's ready row;
- the resend's marker;
- the resend's response.

In each case IPS truthfully had no record of the payment, and exactly one pacs.008 reaches IPS: byte-identical to the stored original, with a callback delivered. In the marker case the payment is investigated again and resent under a second authorization.

**Host tests (2).**
- The runtime waits the nine-second first delay, investigates, receives 1016, resends once and delivers the callback.
- The shipped timeout defaults start with execution enabled.

## Independent reviews

**Standards.** One blocking finding: a new startup rule (IPS request timeout below the investigation call timeout) rejected the shipped defaults. The rule was removed and replaced by giving investigations their own attempt budget; a regression test covers it. The should-fix findings were addressed:
- duplicated pacs.002 constant, response consumption and initial-record predicate;
- missing committed-response ordering guard in ResendRepository;
- stale ledger and 006 open finding;
- triplicated pacs.028 simulator;
- runtime budget ordering.

The nits were addressed, except that `ResendAttempt` keeps `InvestigationId`, `Result` and `TransportFailure` for parity with InvestigationAttempt; only tests read them.

**Spec.** No blocking findings; no path sends a second pacs.008. Addressed:
- status and link consistency;
- a `MaxCycles` test;
- runtime first-delay coverage;
- the `MaxResends` = 3 case;
- callback assertions for rejection;
- comparing the process-test submission to the stored original;
- the deadline reason stored as details rather than a transport failure;
- the documented status source;
- the shutdown budget.

## Verification

- `dotnet build` (Release): 0 warnings, 0 errors.
- **888 tests pass:** 257 unit/architecture/Contracts and 631 integration. The baseline was 864; 24 tests were added (18 resend workflow, 3 process kill, 3 host).
- `dotnet format` and the IDE0005 unused-usings check: clean.
- `dotnet ef migrations has-pending-model-changes`: no changes.

During development, two intermittent failures were seen:
- **Host runtime test:** a test defect. It polled the status route, which acknowledges the outcome and cancels its callback. It now polls SQL and passed six consecutive isolated runs and the full suite.
- **`Pacs008ProcessingTests ... ("unsigned")`:** failed once in a full run, while both review agents were also building and testing. Its error message was not captured. It passed in isolation and in the two later full runs. No cause has been established.

## Remaining

- **Not covered by tests:**
  - shutdown draining of admitted investigations or resends, which reuse the already-tested SupervisedWork;
  - that the runtime runs recovery and processing in separate scopes;
  - process kills at the authorization and outcome commits, which are covered by in-process crash simulations only.
- **Risk before live use:** re-sent signed XML keeps the original signature. Certificate rotation and IPS acceptance of a re-sent signed message must be checked before live activation.
- **Separate capability:** unsolicited incoming pacs.002 is still required for outgoing pacs.008 parity.
- **No change to:** endpoints, Contracts, JSON or event names. Live processing remains disabled by default.
