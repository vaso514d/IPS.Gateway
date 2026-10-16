# Review 007c: incoming camt.056 and camt.029 (acknowledge and archive)

Branch codex/recalls, stacked on 007b (c5b63cf) and the unmerged slices 005a-005d; no merge is approved. On 2026-10-07 the owner chose to acknowledge and archive incoming recalls as the source does, then approved the [specification](../specs/007c-incoming-recalls.md). Commit and merge approval are pending.

## Delivered

- **Archive.** After the sequence check, `IncomingReceiptPreparation` completes a camt.056 or camt.029 receipt (short type or full definition) as processed with no further work. No payment, transfer or reply is created and no remote system is called; the stored receipt and its raw XML are the archive. A missing or non-positive sequence still holds the receipt.
- **Acknowledge.** `IncomingReceiveWorker` acknowledges these two types with the sequence header after the receipt commit, like the other acknowledged types; a failed acknowledgement never stops polling and a redelivery is acknowledged again. Other unsupported types (for example camt.053) stay held and unacknowledged.
- **Rule.** `PaymentMessageTypes.IsArchivedRecall` names the two types once.
- No Contracts change, no migration, no new configuration.

## Changed existing tests

- Two tests that used `camt.056` as an unsupported incoming type (`IncomingCompositionTests`, `IncomingWorkerSqlTests`) use `camt.053`. No assertion was weakened.

## New tests

- Composition (real SQL): each of the four type spellings completes as processed, creates no payment or reply, makes no remote call and keeps its raw XML; a recall with a non-positive sequence is held.
- Worker (real HTTP simulator and SQL): camt.056 and camt.029 are acknowledged after storing, a failed first acknowledgement and a redelivery are acknowledged again with the receipt stored once, and camt.053, delivered first, is stored held and never acknowledged.

## Independent review

One combined Standards and Spec review (the slice is small). No blocking findings: the preparation order, the acknowledgement after commit, the shared discovery and composition handling of a terminal receipt without a payment, and the stand-in change were all verified, and nothing is lost by acknowledging first because the raw XML is stored before the acknowledgement. Fixed: the worker test could pass by timing for an unwanted third acknowledgement (the unsupported type is now delivered first, so any acknowledgement of it would show before the recalls'); the acceptance bullets for a failed first acknowledgement, a redelivery and a stored-once receipt are now tested; a long comment was wrapped. Recorded: nothing alerts a person to an archived recall.

## Verification

- `dotnet build` (Release): 0 warnings, 0 errors.
- Full test run: **1,248 tests pass** (421 unit/architecture/Contracts, 827 integration), against a baseline of 1,242; 6 tests were added.
- `dotnet format` and the IDE0005 check: clean.
- `dotnet ef migrations has-pending-model-changes`: no changes (no migration was needed).

## Remaining

- **The core system is not told** of an incoming recall or refusal. A recall is answered only when something sends a pacs.004 or a camt.029 on its own. Closing this needs a Contracts receive method and an owner decision.
- **Nobody is alerted:** an archived recall is silent, so a recall deadline can pass unnoticed unless the process owner watches the stored receipts.
- **Nothing is verified:** these messages are acknowledged and archived without a schema or signature check, as in the source; anything built on them later must verify them first.
- **No real IPS verification:** that IPS wants only an acknowledgement comes from the source.
- Carried: three test-only production APIs, load-sensitive tests, the naming debt of 005a.
