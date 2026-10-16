# Specification 018: a sample core receiver and a receiver test kit

Status: on codex/core-receiver-sdk, after 017b. On 2026-10-16 the owner accepted all four parts of the client-integration proposal (Contracts additions, the receiver package, documentation, and this sample and test kit) and asked for them to be built. Implemented and uncommitted on the same branch. This specification extends the [rebuild plan](../../rebuild-plan.md), which excludes mock hosts, for exactly these two deliverables.

## Why

`IPS.MiidleWear.AspNetCore` (017b) removes the endpoint work, but a bank still has to write the nine `IClientPaymentReceiver` methods correctly. Two things are hard to get right without help:
- **Idempotency.** A redelivered message must return the first answer.
- **The status query.** It must answer `PDNG` while processing, the stored result afterwards, and 404 only for a message the core never received.

A bank also needs a way to prove its receiver before it meets the real gateway.

## Scope

1. **`samples/IPS.MiidleWear.CoreReceiver.Sample`**: a runnable ASP.NET Core application built only on the two packages.
   - It implements all nine methods over an in-memory store keyed by `IIpsReceiveContext.IdempotencyKey`.
   - It shows ACCP and RJCT with reason codes, `PDNG` for a payment still being posted, the stored answer on redelivery and in the status query, 404 for an unknown reference, a status callback, and a reversal notification.
   - Configuration chooses the answer per kind (accept, reject with a code, or answer `PDNG` first), so a bank can watch each path.
   - It is not shipped as a package and is not a production pattern for storage.
2. **`src/IPS.MiidleWear.Testing`**: a packable .NET library a bank runs from its own tests.
   - `IpsReceiverConformance.RunAsync(HttpClient client, ...)` sends what the gateway sends, with the gateway's JSON (`null` members included) and Idempotency-Key, to a receiver at the client's base address.
   - The receiver can be in process (`WebApplicationFactory`) or deployed (a URL).
   - It returns one result per check, with a reason for each failure.

## The checks

For each of the seven receive kinds, with a fresh unique message each run:
1. **The answer is usable.** It is 2xx with `Status` `ACCP` or `RJCT`; returned `Id`, `EndToEndId` and `TxId` equal the request; there are no duplicate members.
2. **Redelivery.** The same body and key again gives the same `Status`.
3. **Status query.** `messageKind` and the key give the same outcome as the receive answer.

Once:
4. **Unknown reference.** The status query for a reference never sent answers 404, not 204 or 200.
5. **Status callback.** An outgoing status (`{clientReference}:{status}`) and a reversal (`in:{coreReference}:{status}`) are each accepted with 2xx, and again on redelivery.
6. **Null members.** A request with explicit `null` optional members is accepted.

The kit applies the gateway's reading rules: case-insensitive top-level names, duplicates rejected, `ACCP`/`RJCT` final. A test in this repository runs the kit and the gateway's real interpreter on the same answers and requires the same verdict, so the kit cannot pass what the gateway would not accept.

## Dependencies and layout

- `IPS.MiidleWear.Testing` references Contracts only (base class library, `System.Net.Http`, `System.Text.Json`). It has no test framework dependency, so it works with xUnit, NUnit or MSTest.
- The sample references `IPS.MiidleWear.AspNetCore`. Neither project is referenced by the gateway.
- The architecture rules admit `IPS.MiidleWear.Testing` with the Contracts reference only.
- The sample sits under `samples/` and is in the solution.

## Acceptance

- **Sample.** The sample passes every kit check.
- **Kit against faulty receivers.** Each of these fails the named check with its reason:
  - 204 for an unknown reference;
  - a new answer on redelivery;
  - a mismatching echoed `EndToEndId`;
  - an enum read failure on the status callback (a receiver that rejects string enums);
  - an answer with a duplicate member.
- **Kit and gateway agree.** For the same answers, the kit and the gateway's interpreter reach the same final-or-unknown verdict.
- **Packaging.** `IPS.MiidleWear.Testing` packs with a readme that shows an xUnit example.
- **Verification.** Release build, the full suite (`-m:1`), format, `git diff --check`, and independent Standards and Spec reviews.

## Not in scope

- Signing and certificates (a later specification).
- A command-line runner for the kit.
- Load or timing checks. The kit does not measure the IPS deadline.

## Risks

- **A second copy of the reading rules.** The kit re-implements the gateway's interpretation in a few lines. The agreement test pins it.
- **Fidelity of the sample messages.** The kit's sample messages are realistic but synthetic. A receiver can still meet real content that the samples do not cover.

## Implementation notes (2026-10-16)

- **Kit API.** `IpsReceiverConformance.RunAsync(HttpClient, IpsReceiverConformanceOptions?, CancellationToken)` returns `IpsReceiverConformanceResult`:
  - `Checks`, `Failures` and `Passed`;
  - `EnsurePassed()`, which throws `IpsReceiverConformanceException` with every failed check;
  - `ToString()`, one PASS or FAIL line per check.

  The options set the bank's BIC, a test credit account and the currency.
- **Checks.** There are 26: three per kind, plus 404, plus the status callback and the reversal twice each. A transport failure is recorded as a failed check, not thrown.
- **No separate null-member check.** Every request is serialized like the gateway's `JsonContent`, with `null` members, so the "null members" check is covered by every receive check.
- **Sample.** `SampleApp.Create(args, configure)` builds the application; a named entry point avoids a second global `Program`. The ledger forgets a failed posting so a redelivery tries again.
- **Tests (`ReceiverTestKitTests`).**
  - The sample passes all 26 checks and shows PDNG while posting.
  - Six faulty receivers each fail the named check. The faults are injected by a startup filter in front of the sample: 204 for an unknown reference, a new answer on redelivery, a wrong `EndToEndId`, a rejected status callback, a duplicate member, and an `Id` on a pacs.008.
  - Thirteen pacs.008 answers and six transfer answers get the same final-or-not verdict from the kit and from `IncomingCoreReplyInterpreter`.
