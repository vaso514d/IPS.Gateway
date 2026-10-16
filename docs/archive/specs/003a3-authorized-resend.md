# Review 003a.3: protocol-authorized resend and investigation runtime

Status: on 2026-10-06 the owner approved the recommended decisions and asked for implementation. Implemented on codex/outgoing-resend, base 78d1c6b; the owner approved the commit and merge into codex/capability-rebuild on 2026-10-06. See the [review evidence](../reviews/003a3-authorized-resend.md).

## Scope

- When a trusted investigation reports 1016 (no transaction found), resend the outgoing pacs.008 once per such report, up to `MaxResends`.
- Run investigations and resends from the existing supervised `OutgoingRuntime`.
- Remove the `InvestigationExecution` seam.

No new endpoint, Contracts, JSON, event name or callback rule changes. Unsolicited incoming pacs.002 stays a separate review. Live processing stays disabled by default (`Payments:Outgoing:Execution:Enabled`).

## Source evidence

Pinned source d498de6c4638aa71cdb20189d13642b41abab5f1, `IPS.MiidleWear.API/Transactions/TransactionRecovery.cs`:

- `NotFound` while `ResendCount < MaxResends`: Investigating → Resending, `CountResend`, then the pacs.008 is resent immediately, without `X-MONTRAN-RTP-PossibleDuplicate`.
- `NotFound` once resends are used up: ManualReview ("IPS has no record of the transaction and it was already resent N time(s)") with a callback obligation.
- Resend result:
  - accepted or rejected becomes final, with source Investigation, plus a callback obligation;
  - anything else returns to Uncertain with the next investigation delay, taken from the investigation count.
- A resend increments only `ResendCount`; `AttemptCount` (`MaxAttempts`) counts investigations only.
- Stuck Resending is recovered to Uncertain and investigated again; a marked resend is never repeated.
- The source rebuilds and re-signs the XML with the same MsgId/TxId (`IsoTransactionSender`).

## Owner decisions (2026-10-06)

1. **First-delay paths.** Keep the source paths approved in 003a.2: nine seconds after normal uncertainty, immediate after ownership recovery.
2. **Counters.** `MaxCycles` counts investigations only. Resends are limited separately by `MaxResends` (default 3; 0 disables resending). Counters are never reset.
3. **Window and resend timing.** The 24-hour deadline frozen on the first investigation (003a.2 decision 3: no new call at or after the deadline, saved evidence replayed first) also bounds resends.
   - 1016 authorizes a resend outside the initial 20-second submission window.
   - The resend sends the exact stored original pacs.008 bytes with the original identifiers. This differs from the source, which rebuilt and re-signed.
   - A lost resend response leads to another investigation, never a repeat of the marked resend.
4. **Reply matching.** Replies are matched by exact message definitions, already enforced by the 003a.1 protocol slice. A resend response is interpreted with the original pacs.008 correlation through the existing `IpsReplyInterpreter`.

## Durable model

New table `OutgoingResends` (`ResendRow`):

| Column | Meaning |
|---|---|
| `Id`, `PaymentId` | Identity; alternate key (`Id`, `PaymentId`) |
| `Number` | Ordered resend number, > 0, unique per payment |
| `InvestigationId` | The NotFound investigation that authorized it; **unique**, which makes the authorization one-use in SQL |
| `CreatedAtUtc` | Authorization time |
| `Outcome`, `DetailsJson`, `TransportFailure`, `CompletedAtUtc` | Result, written once |

The investigation FK is composite (`InvestigationId`, `PaymentId`), so an authorization cannot cross payments.

Changes to `OutgoingMessages`:

- Add a nullable `ResendId`, FK (`ResendId`, `PaymentId`).
- Each resend has one outbound row (definition pacs.008.001.12, the exact original content and disposition, `OriginatingMessageId` = the original outbound row) and at most one response row.
- New filtered unique index (`ResendId`, `Direction`).
- The initial-record index and the lifecycle check constraint now require `InvestigationId IS NULL AND ResendId IS NULL`, and a row may not belong to both an investigation and a resend.
- `OutgoingJournal`, `PaymentPreparationRepository` and `PaymentSubmissionRepository` filter initial records explicitly.

The migration is generated with the EF CLI; fresh databases only.

## Workflow

**Investigation (changed `OutgoingInvestigation.FinishAsync`).** On NotFound:

- if resends remain, one commit does all of the following:
  - stages the result;
  - Investigating → Resending (source Investigation, carrying the 1016 details);
  - stages the resend authorization;
  - releases the claim due now.
- otherwise ManualReview with the source description and callback obligation.

The deadline is checked once, by the resend, before dispatch.

The NotFound exclusion in investigation discovery and in `ProcessAsync` is removed: a NotFound result is never left on an Uncertain payment.

**Resend (new `Payments/Investigation/OutgoingResend.cs`).** It handles Resending pacs.008 payments, uses `ClaimedPayment` like the other workflows, and reads the latest resend first.

1. Replay a saved response and interpret it before any deadline check.
2. Marker with no response: record the resend as abandoned, then Uncertain (Recovery), due now, as the source does for stuck work. Never resend again.
3. Deadline reached: ManualReview with callback obligation.
4. The original is development-unsigned and current policy no longer allows that: release and retry after `PreparationRetryDelay`.
5. Stage the outbound row (exact original bytes), then commit.
6. Stage the `SendStarted` marker, then commit.
7. Send through `IIpsTransport`, with the call clamped to the remaining window and `CallTimeout`.
8. Save the complete response within the persistence budget, then interpret (status source Investigation, as for every outcome the investigation workflow records; the original returned unresolved resends to Uncertain with source Recovery, which discovery does not distinguish):
   - accepted or rejected becomes final, with source Investigation;
   - unknown becomes Uncertain with the next delay `RetryDelay(latest investigation number)`, capped at the deadline.
   - A transport failure after dispatch records the failure and goes to Uncertain the same way.

Every step commits before the next. No transaction spans I/O. Parent row version and committed ownership fence every checkpoint.

**Recovery (`OutgoingTransactionWork.IsResumableAsync`).** An expired Resending claim is always released as Resending. The next resend owner replays a saved response, records an abandoned marker (step 2), or continues an unsent authorization. Because of this, every resend row receives a result before the payment leaves Resending.

## Runtime

`OutgoingRuntime.ProcessAsync` routes by status after recovery:

| Status | Workflow |
|---|---|
| Received, Sending | `Pacs008Processing` |
| Uncertain, Investigating | `OutgoingInvestigation` |
| Resending | `OutgoingResend` |

A third supervised sweep runs at `Investigation:DiscoveryInterval` (5 s) and queues:

- `IInvestigationRepository.FindDueAsync`;
- `ITransactionWorkRepository.FindDueAsync(Resending)`.

The existing per-payment admission keeps one in-process run per payment; SQL claims remain the authority. `InvestigationExecution` is deleted.

Investigations and resends run under their own `Investigation:AttemptBudget`, not the execution attempt budget, so the outer budget cannot cancel a call before its evidence is saved. The extended host shutdown timeout also covers the investigation persistence budget. No new startup rule is added.

`MaxResends` is added to `InvestigationOptions` and appsettings (`Payments:Outgoing:Investigation:MaxResends`, default 3, must be ≥ 0).

## Acceptance

Real SQL, independent IPS fixtures:

- **1016 resends once.**
  - A simulator that never received the original receives exactly one pacs.008, byte-identical to the original, with the same MsgId/TxId and no PossibleDuplicate header.
  - Accepted or rejected becomes final with history and a callback obligation.
- **Resend counting.**
  - With `MaxResends` = 3, a fourth 1016 gives ManualReview with a callback obligation.
  - `MaxResends` = 0 goes straight to ManualReview.
  - `MaxCycles` counts investigations only.
- **One-use authorization.**
  - A second resend for the same investigation violates the SQL unique index.
  - Two runtimes or a stale owner cannot both dispatch.
- **Lost resend response.** The next action is an investigation, not another pacs.008. The simulator that processed the resend then lost its reply resolves through pacs.028.
- **Crash points.**
  - Kill the process around authorization, outbound row, marker, response and interpretation.
  - A restart resumes or investigates according to recovery, and never sends twice.
- **Deadline.** Saved response replay precedes the deadline; no resend at or after the deadline; the call is clamped to the remaining time.
- **Development-unsigned originals.** They follow current policy.
- **Runtime.** The runtime discovers due investigations and resends, respects first-delay and recovery paths, and drains on shutdown.
- **Existing behavior unchanged.** Initial submission, status reads and response replay are unaffected by resend journal rows.

Run build, full suite including process tests, format, Contracts/architecture checks and EF model/migration consistency. Present evidence and wait for owner approval before merging.

## Not in scope

- Unsolicited incoming pacs.002 (required for full outgoing parity).
- Real IPS interoperability.
- Certificate rotation for re-sent signed XML: the exact bytes keep the original signature. Verify against IPS before live activation.
- Production activation.
