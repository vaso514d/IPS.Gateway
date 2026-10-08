# Review 008a: incoming pain.001 (payment initiation)

Branch codex/payment-initiation, stacked on 007c (a951787) and the unmerged slices 005a-005d and 007a-007b; no merge is approved. On 2026-10-07 the owner approved the slicing (incoming pain.001 first) and the long-key rule (a `PmtInfId` over 31 characters is held), then the [specification](../specs/008a-incoming-pain001.md). Commit and merge approval are pending.

## Delivered

- **Verify and store.** `IncomingPain001Protocol` checks the envelope, the definition (`pain.001.001.12` and the registry spelling `.012`), the trusted IPS signature, exactly one payment instruction with one transaction, the schema, and the mandatory fields: a `PmtInfId` of at most 31 characters, the group message id, a valid creation time (kept with its own offset), the debtor's agent by BICFI (which must be us) and an instructed amount. Anything else holds the receipt and delivers nothing. A valid initiation is stored once as an `IncomingTransfer` of kind `pain.001`, keyed by participant BIC and `PmtInfId`.
- **Content.** `IncomingPain001` carries the group, initiating party, payment type codes, requested execution date (a date or a date-time), debtor with account and agent, ultimate parties, instruction and end-to-end ids, amount, creditor, purpose and remittance, using the same party shapes as a pacs.008.
- **Hand over and recover.** The `PmtInfId` is the CBS idempotency key and status reference (`messageKind=Pain001`); delivery, the 404 re-post, the backoff and the 24-hour window are the shared 005c and 005d engine. A core reply is accepted only if the `Id` it names is the `PmtInfId`.
- **Acknowledge.** `MessageAck` after the receipt commit, like the other types. The core answers IPS itself, by a pacs.008 with EndToEndId `PSP-` plus the `PmtInfId` (already possible) or, with 008b, a pain.002.
- **Shared reading.** The party, account, agent, address and remittance reading of the incoming pacs.008 moved into `IncomingPartyReader`, which takes the document namespace; the pacs.008 mapping delegates to it with unchanged behavior. The list-compare helper became a shared `ValueList<T>` so a redelivered initiation with structured remittance compares equal after storage.
- No Contracts change and no migration: the kind column holds any kind.

## Changed existing tests

- None changed. The helper that registers incoming transfers now knows the third protocol.

## New tests

- **Reader (Java-signed fixtures):** a full initiation (every mapped field, the creation offset kept) and a minimal one (date-time execution date, non-IBAN account, optional parts absent); each unverifiable case (definition, signature, schema, two payment instructions, missing initiation id, an id of 32 characters, missing message id, debtor agent without BICFI, not our agent, malformed); 31 characters accepted and 32 held.
- **Identity:** a redelivery with structured remittance is the same transfer, changed content is held, and the same key as a pacs.009 is a separate transfer.
- **Delivery:** the `PmtInfId` as key, a reply naming another id refused and one naming it accepted.
- **Worker:** pull, store, acknowledge and hand the initiation to the core over HTTP, then ask its status as `Pain001`.

## Independent reviews

**Standards.** No blocking findings; the incoming pacs.008 mapping is unchanged by the extraction of `IncomingPartyReader` (every moved method and the helper semantics were compared with the removed ones). Fixed: the hold tests now assert each case's own reason (several mandatory fields are caught by the schema first, which the test now says); a named predicate `IsIncomingTransfer` replaces repeated three-way conditions; the header definition must be one of the two definitions rather than any pain.001 spelling; the registry spelling is a named constant and is tested; a unit test for the matchers. Recorded: a few one-line pass-through helpers remain in `IncomingPacs008Mapping` and `IncomingPacs008`; the schema loader takes the pain.001 namespace from the protocol class instead of a payments XML class.

**Spec.** No blocking divergence from the source. Fixed: a creation time that is out of range is held instead of escaping the reader; an `xs:date` with a time zone leaves the execution date out (as the source does) instead of holding the whole initiation; tests for the registry definition, a time-zoned date, a missing and an out-of-range creation time. Recorded: an id of 36 characters or more fails the schema before the 31-character rule, so it is held with the generic reason; the ACCP/RJCT, 404 re-post, backoff and window cases for pain.001 rely on the shared engine's tests, with ACCP and the wrong-id refusal tested here.

## Verification

- `dotnet build` (Release): 0 warnings, 0 errors.
- Full test run: **1,280 tests pass** (432 unit/architecture/Contracts, 848 integration), against a baseline of 1,248; 32 tests were added. An earlier full run had one failure in the existing `IncomingCbsProcessingTests` (it passes alone and in the final run).
- `dotnet format` and the IDE0005 check: clean.
- `dotnet ef migrations has-pending-model-changes`: no changes (no migration was needed).

## Remaining

- **The initiation deadline is not enforced.** IPS rejects an unanswered initiation on its own Timeout Deadline (counted from the creation time); a core answer that arrives later has no effect on IPS and is not tracked here.
- **No link** between a later `PSP-` pacs.008 and the stored initiation, as in the source.
- **No real IPS verification.** The acknowledgement-only behavior, the key and the definition spelling come from the source.
- The core system must implement the pain.001 receive route and answer status queries for `messageKind=Pain001`.
- Outgoing pain.002 (008b) and camt.055 (008c) remain. Carried: three test-only production APIs, load-sensitive tests, the naming debt of 005a.
