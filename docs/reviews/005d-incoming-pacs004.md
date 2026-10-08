# Review 005d: incoming pacs.004

Branch codex/incoming-pacs004, stacked on codex/incoming-pacs009 (87bbaf7), codex/outgoing-pacs004 (e65c3a5) and codex/outgoing-pacs009 (fc82368); none of those merges is approved. On 2026-10-06 the owner approved the three decisions (a generalised transfer aggregate, the return id as the identity, no link to the outgoing payment) and the [specification](../specs/005d-incoming-pacs004.md); commit and merge approval are pending.

## Delivered

- **Generalised aggregate.** `IncomingFiTransfer` became `IncomingTransfer` with a `Kind` (`pacs.009`, `pacs.004`) and a business `Key` (EndToEndId or return id). The state machine, events, claim and recovery are shared; identity is participant BIC, kind and exact key (binary collation, byte-length unique index), so a pacs.009 and a return with the same text are two transfers.
- **Shared contracts.** `IIncomingTransferContent` (kind, key, receiver, the identifiers a core reply may echo) is implemented by `IncomingPacs009` and the new `IncomingPacs004`; `IIncomingTransferProtocol` reads one kind each and `IncomingTransferRegistration` (was `IncomingPacs009Processing`) registers any kind. The stored content is read back by kind.
- **Reading a return.** `IncomingPacs004Protocol` checks the envelope and definition, the trusted IPS signature, one returned transaction, the schema, and the mandatory fields: a return id equal to the group message id, the original transaction id, and both agents by BICFI. Field rules follow the source mapper and the profile of the outgoing builder. The receiver is the instructed agent, which must be our participant; the reason code is passed through as received; an absent value date stays absent.
- **Delivery.** One POST to the new `Pacs004SubmissionPath` with the return id as the idempotency key; the status query asks `messageKind=Pacs004` for the return id; recovery, 404 resubmission, backoff and the 24-hour window are the shared 005c behavior. A core reply is accepted only if the `Id` it names is the return id.
- **Acknowledge.** `MessageAck` after the receipt commit, like the other types; no pacs.002; no link to the original outgoing payment.
- **Schema.** One migration (`IncomingTransferKinds`, EF CLI) renames the key column and adds the kind column and the new unique index.

## Changed existing tests

- 005c tests follow the renames (`IncomingTransfer`, `IncomingTransferRegistration`, the generic protocol and core client) and register both protocols; `IncomingFiTransferTests` is now `IncomingTransferAggregateTests`. No assertion was weakened.
- One deliberate relaxation, tested for both kinds: an identifier the transfer does not carry is no longer compared with the core's answer.

## New tests

- **Reader (Java-signed fixtures):** a full return and a minimal one; alternative forms (`Othr` account, `Prtry` reason, several additional-info lines, absent value date); each unverifiable case (definition, signature, schema, two returns, not our participant, return id differing from the message id, missing return id, missing original transaction id, agent without BICFI, malformed).
- **Identity:** a redelivery, conflicting content held, two returns of one payment as two transfers, a pacs.009 and a return with the same key as two transfers.
- **Delivery and recovery:** the return id as key, only that id checked in the answer, 404 resubmission of the same request, the window end; the shared backoff, owner and crash cases are covered by the pacs.009 tests on the same code.
- **Worker:** pull, acknowledge, hand the return to the core, and ask its status by `Pacs004` over HTTP.
- **pacs.009 relaxation:** a pacs.009 without a transaction id accepts an answer that names one.

## Independent reviews

**Standards.** No blocking findings. Fixed: the transfer kind is no longer derived with a pacs.009 fallback (each protocol says which message types it reads, and an unknown kind throws in the status query); the stale `EndToEndId` wording and a long comment; a nested ternary; the missing pacs.009 relaxation test. Recorded: the envelope and signature steps of the two protocols are duplicated and should be extracted when a third protocol arrives.

**Spec.** No blocking divergence. Fixed: agents are read as the source does (group-level agents, else `OrgnlTxRef`); only the first additional-info line is kept; the original transaction id is trimmed; the missing return-id hold, alternative wire forms and the window-end tests were added. Recorded: the return's backoff, owner and crash cases rely on the shared engine's tests; there is no settings test for `Pacs004SubmissionPath` (the other paths are validated the same way).

## Verification

- `dotnet build` (Release): 0 warnings, 0 errors.
- Full test run: **1,123 tests pass** (357 unit/architecture/Contracts, 766 integration), against a baseline of 1,103; 20 tests were added.
- `dotnet format` and the IDE0005 check: clean.
- `dotnet ef migrations has-pending-model-changes`: no changes; the migration was generated with the EF CLI.

## Remaining

- **No real IPS verification.** That IPS wants only a `MessageAck` for a return, and the core's key for it, come from the source only. The source handler passes the original EndToEndId as the key while its own reference helper prefers the return id; the core owner should confirm the return id.
- The migration gives existing rows an empty kind, valid only because 005c was never deployed.
- The core system must implement the pacs.004 receive route and answer status queries for `messageKind=Pacs004`.
- Carried: three test-only production APIs, load-sensitive tests (the Java-started signature tests and an `OutgoingStatusDeliveryTests` case), the naming debt of 005a.
