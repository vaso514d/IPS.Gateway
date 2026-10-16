# Review 005a: outgoing pacs.009 and the shared outgoing core

Status: implemented for review on codex/outgoing-pacs009, base 14bc5a4. The owner approved the slicing and three decisions, then this specification, on 2026-10-06; merge approval is pending. See the [review evidence](../reviews/005a-outgoing-pacs009.md). Later reviews, each its own slice: 005b outgoing pacs.004, 005c incoming pacs.009, 005d incoming pacs.004.

## Scope

- Add outgoing pacs.009 (FI credit transfer) end to end: intake, validation, XML, signing, send, outcome, CBS callback, recovery.
- Generalise the pacs.008-only plumbing once, because pacs.009 is the second feature that shares it (coding style: extract only after two features share a behavior). Outgoing pacs.004 (005b) then reuses it.
- No Contracts change (the DTOs and routes already exist), no incoming change, no pacs.004. Live processing stays disabled by default.

## Source evidence

Pinned source d498de6c4638aa71cdb20189d13642b41abab5f1 (IPS.MiidleWear.*): `API/Controllers/GatewayController.cs` (`SendPacs009Async`), `API/Transactions/IsoTransactionSenders.cs` and `IsoTransactionSender.cs`, `API/Transactions/OutgoingTransactionDispatcher.cs`, `API/Transactions/TransactionRecovery.cs`, `API/Services/Pacs009XmlMessageBuilder.cs`, `Pacs009PaymentInstructionMapper`, `Application/Validation/Pacs009PaymentRequestValidator.cs`, `API/Transactions/IpsStatusReply.cs`; tests `Api/Pacs009SchemaTests.cs`, `IpsV1FieldProfileTests.cs`, `MockIpsEndToEndTests.cs`, `TransactionRecoveryTests.cs`.

- Route `POST /api/ips/pacs009/send`, DTO `Pacs009PaymentRequestDto`. Message id = `Id`; transaction id = `TxId ?? Id`; both are supplied by the caller (pacs.008 ids are generated).
- Validation: ClientReference, Id, both agents, EndToEndId, ValueDate, enabled currency and amount > 0 are required. Id/InstrId/EndToEndId/TxId are at most 35 ASCII characters. The debtor agent must be our BIC or an indirect participant. Priority 0 or 1; FromTime before RejectTime; CategoryPurpose a 1–4 character code or proprietary up to 35; Purpose up to 35 (a 4-letter uppercase value is a code, otherwise proprietary); AddPurpose unbounded; accounts are IBANs with a valid checksum.
- XML `pacs.009.001.11` (`FICdtTrf`) with `head.001.001.03`; parties are financial institutions (BICFI only), accounts IBAN-only, settlement date per transaction, no UETR, InstrPrty, ClrSysMmbId, InstdAgt or BizSvc (`IpsV1FieldProfileTests`).
- IPS reply is a pacs.002 interpreted with the same status table as pacs.008 (ACCP/ACTC/ACSC accept, RJCT rejects, otherwise Uncertain, reason NARR when none, IPS code from `X-MONTRAN-IPS-ReqSts`). No inbound reply from us exists for outgoing messages.
- No 20-second submission deadline, no TM01/1015 pre-send rejection and no currency amount limits: those are pacs.008 only.
- Recovery: there is no pacs.028 for pacs.009/004. An unknown outcome is resent with `X-MONTRAN-RTP-PossibleDuplicate: true` so IPS answers with the original's status. Backoff 30 s, 1 min, 5 min, then 15 min; window 24 hours from creation; stuck work is recovered after 35 s; attempts or window exhausted go to ManualReview with a callback.

## Owner decisions (2026-10-06)

1. Slice: outgoing pacs.009 plus the shared generalisation first; then pacs.004, incoming 009 and incoming 004 separately.
2. HTTP result: the same as pacs.008 — 200 with the final status, or 504 with the current status after the wait; a duplicate ClientReference returns the current status at once. This replaces the original's 202.
3. Recovery: follow the original — an unknown outcome is resent as a possible duplicate.

## Shared outgoing core (generalisation)

Application, `Payments/` (names indicative; the review decides final placement):

- `AcceptedPayment` — abstract record base for the intake snapshot: `EndToEndId`, `SubmissionDeadlineUtc` (nullable; null means no pre-send deadline). `AcceptedPacs008` derives from it; `AcceptedPacs009` is new.
- `IOutgoingMessageProtocol` — the only per-type seam: `MessageType`, `MessageDefinition`, `BuildUnsignedXml(AcceptedPayment, messageId, transactionId)`, `SignAsync` and `Correlation(AcceptedPayment, messageId, transactionId)`. `IPacs008MessagePreparation` becomes the pacs.008 implementation of it.
- `Pacs008Processing` becomes the type-agnostic `OutgoingPaymentProcessing`, selecting its protocol by the payment's message type; the checkpoint order, markers, claims and deadline logic are unchanged. The deadline check runs only when `SubmissionDeadlineUtc` is set. Existing tests are updated mechanically for the rename.
- `PaymentMessageTypes` gains `Pacs009`, `Pacs009Definition` (`pacs.009.001.11`) and matchers.

Infrastructure:

- The `MessageType == pacs.008` gates become "is a supported outgoing type": `PaymentOwnership.OwnedPacs008` (renamed `OwnedOutgoing`), `PaymentSubmissionRepository`, `OutgoingPaymentRepository.Add`, `OutgoingStatusOutbox` (so pacs.009 gets CBS callbacks) and `OutgoingRuntime` routing; `TransactionWorkRepository` keeps its pacs.008-first ordering, which already suits every outgoing type.
- The journal writes the message definition of the payment's type instead of the pacs.008 constant (`PaymentPreparationRepository`, `ResendRepository`).
- `PaymentJson` reads and writes the accepted snapshot by message type.
- `IpsReplyInterpreter` keeps its definition parameter; `StatusReportProtocol` passes the payment's definition so unsolicited pacs.002 reports about a pacs.009 also work.
- One additive EF migration (EF CLI) widens `CK_Transactions_Accepted`, `CK_Transactions_Preparation` and the `OutgoingMessages` lifecycle check to the new type, and adds the resend attempt column changes below.

## pacs.009 content

- Application `Payments/Pacs009/`: `Pacs009Request` (record, init properties), `ValidatedPacs009`, `Pacs009Validator` and `Pacs009Policy` (participant BIC, enabled currencies, indirect participants; from the existing `Payments:Outgoing:Policy` section), `Pacs009Intake`, `AcceptedPacs009`, `Pacs009ProtocolProfile` (IPS BIC from `Payments:Outgoing:Protocol:IpsBic`). The rules above are checked once, in intake, as for pacs.008.
- Identifiers: message id = request `Id`, transaction id = `TxId ?? Id`. Both are stored on the payment row, whose unique indexes keep them unique. Intake rejects an `Id` or `TxId` already used by another payment as a validation error (`FindByMessageIdAsync`); the unique indexes fence a race.
- Infrastructure `Payments/Pacs009/`: embedded `pacs.009.001.11` schema, `Pacs009Xml` (ported from the source builder and mapper), `Pacs009Message` and the signer overload; `Pacs009Preparation : IOutgoingMessageProtocol`.
- API: `OutgoingPaymentsController` gains `POST Pacs009RestApiRoutes.Send` with `Pacs009RequestMapping`; the status route is shared. Execution must be enabled, as for pacs.008. `IOutgoingExecution`/`OutgoingSubmission` take a payment-kind request instead of `Pacs008Request`.

## Recovery by possible-duplicate resend

- A pacs.009 whose send ends unknown becomes Uncertain, due 9 seconds later (first delay) or at once after abandoned-ownership recovery, as for pacs.008.
- `OutgoingDuplicateResend` (new workflow): under a claim, it journals a resend of the exact stored original bytes and disposition, commits a `SendStarted` marker, then sends with `X-MONTRAN-RTP-PossibleDuplicate: true`. `IIpsTransport` gains `ResendAsync`, the flagged variant of `SendAsync`; the HTTP client adds the header only there, so pacs.008 resends after 1016 stay unflagged as in the source.
- Each attempt is one-shot per marker and never repeated under the same marker. Unlike pacs.008, a marked attempt whose reply is lost is not terminal: the payment becomes Uncertain and a new attempt is authorized after the next backoff, because the flagged message is idempotent at IPS.
- Attempt rows reuse `OutgoingResends`: `InvestigationId` becomes nullable, with the unique one-use link kept for pacs.008 only; `(PaymentId, Number)` stays unique. A new attempt is created only after the previous attempt has a recorded result. The payment is Resending while an attempt is in flight.
- Interpretation of the reply is the existing pacs.002 interpretation: accepted or rejected is final (source Ips) with a callback; anything else schedules the next attempt, after 30 s, 1 min, 5 min, then every 15 min. `MaxAttempts` 0 means window-limited, preserved from the source.
- The window is 24 hours from creation, frozen on the first attempt. At or after it, the payment goes to ManualReview with a callback; a saved reply is always interpreted first. A call is clamped to the remaining window.
- New options bind under `Payments:Outgoing:Recovery` (delays, repeat, window, max attempts) with the same ordering rules as the investigation options.
- `OutgoingRuntime` routes by message type: pacs.008 as today; pacs.009 Uncertain/Resending to `OutgoingDuplicateResend`. The investigation sweep also discovers due pacs.009 recovery work.

## Acceptance

Real SQL and independently signed IPS fixtures:

- Intake: every validation rule above, duplicate ClientReference, duplicate `Id`/`TxId`, race on the unique indexes.
- XML: schema validity, the IPS v1 field profile, signing, byte-stable resume of unsigned/signed/marker checkpoints, crash at each checkpoint.
- Outcomes: accepted, rejected, unknown reply; callback created atomically; 200 final, 504 timeout with current status, immediate 200 for a duplicate.
- Recovery: possible-duplicate header present on resends and absent on the first send; backoff schedule, window and `MaxAttempts` boundaries; a lost reply schedules a new attempt, never a repeat under one marker; competing owners, stale owner, cancellation, process kill around authorization, ready, marker and response.
- pacs.008 behavior is unchanged: the full existing suite passes with only mechanical updates (rename, constructor arguments) listed in the review.
- Unsolicited pacs.002 about a pacs.009 settles it.
- Build, full tests, format, EF model and migration chain checks, independent Standards and Spec reviews.

## Implementation notes (deviations from the first draft)

- pacs.009 reuses `Pacs008Policy` (participant BIC, enabled currencies, indirect participants) instead of a new `Pacs009Policy`: its three settings are the same for every outgoing type.
- Recovery timing reuses `InvestigationOptions` (first delay, backoff, window, ownership and budgets; `MaxCycles` caps possible-duplicate attempts, 0 meaning window-limited) instead of a new `Recovery` section, so one configuration governs both recovery paths.
- `Pacs008Options` is still the shared ownership, retry and persistence-budget options type; only its `SubmissionWindow` is pacs.008-specific. Renaming it would touch every test and is left for a later clean-up.
- `OutgoingResend` and `OutgoingDuplicateResend` share one `ResendExchange` (send under a committed marker, interpret, abandon, stop at the deadline); each keeps only its own authorization and scheduling policy. Hence `ResendRun` carries who is credited with the outcome and whether an abandoned attempt is investigated at once or waits its backoff.
- Known naming debt left on purpose: `Pacs008Policy`, `Pacs008Options`, `Pacs008Schema`, `Pacs008MessageSigner` and the `Payments/Pacs008` schema folder now serve every outgoing type, and the duplicate resend asks `IInvestigationProtocol.MaySend` about the signing policy. Renaming them touches every test and is left for a clean-up review.
- `IAcceptedPayment` exposes `EndToEndId`, `SubmissionDeadlineUtc` and `SuppliedIds`, implemented explicitly so they never enter the stored JSON snapshot. `SuppliedIds` carries the caller-chosen pacs.009 message and transaction ids.
- The interpreter takes the expected original message definition from `IpsReplyCorrelation`, so unsolicited pacs.002 reports about a pacs.009 are verified against its definition.
- Resend attempts for message types without an investigation store their frozen deadline on the attempt row (`DeadlineUtc`); a pacs.008 resend keeps its investigation link instead, and SQL requires exactly one of the two.

## Not in scope

pacs.004, incoming pacs.009/004, recalls, real IPS interoperability (the header name and idempotent answer come only from the source), production activation.
