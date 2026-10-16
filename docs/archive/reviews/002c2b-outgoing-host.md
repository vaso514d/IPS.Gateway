# Review 2c.2b — Outgoing endpoints and supervised execution

Branch codex/outgoing-host; base03f4976 (owner-approved transport merge). Owner approved committing this increment on 2026-10-05. Merge approval remains pending. No schema change or production activation.

## Delivered

Opt-in POST pacs.008 returns200 for committed final outcomes,504 at the HTTP wait deadline when unresolved, and immediate200 for existing references before new-request validation. GET status preserves exact-outcome acknowledgement and404 behavior. POST outcomes never suppress durable callbacks. Api performs explicit DTO mapping; Application owns validation and submission coordination.

OutgoingRuntime supplies fresh scopes and bounded supervised admission independent of HTTP cancellation. Normal requests bypass channels. SQL discovery refills a bounded payment-ID recovery channel and competes with request execution through the same claims. Abandoned markers without responses become uncertain; saved evidence resumes without another original send. Callback discovery/admission is separate. Shutdown stops admission/discovery, drains, then cancels service execution while existing bounded persistence protects observed evidence. All tasks are tracked and failures logged.

Source evidence at d498de6: GatewayController, TransactionStatusReader, TransactionStatusMapping, OutgoingTransactionIntake, IpsHttpStpClient and callback delivery tests, plus the previously approved synchronous HTTP design. Metadata exception is limited to SendPacs008Async successcode/description and its explicitly edited baseline member. Original source-manifest and JSON fixtures remain unchanged; there is no assembly-driven baseline refresh.

## Verification

- 19 SQL-host tests pass: final accepted/rejected200 with callbacks, two-host duplicate/claim competition, disconnect, unresolved/lost-reply504, invalid400/not-found404, bounded admission/recovery, null-list validation/duplicate bypass, dispose-before-stop and invalid startup settings.
- 7 real-process crash cases pass: intake, unsigned XML, send-ready XML, submission marker, response, outcome and remotely processed/lost reply. Each kills a child process at the committed checkpoint, then starts a new process with no in-memory queue. No initial payment is submitted twice; marker-only uncertainty is retained.
- 3 new Application tests pass: slow SQL observation consumes HTTP budget and returns the committed fallback, caller cancellation leaves admission independent, and duplicates need no new admission/read.
- Full suite: **815 tests passed**, comprising 252 unit/architecture/Contracts and 563 integration tests, with zero failures or skips. Real SQL tests ran against LocalDB. Release build passed with zero warnings/errors; formatting verification, EF migration/model consistency and diff whitespace checks passed. Host smoke checks are included. Local test reports: `tests/IPS.Middleware.Tests/TestResults/outgoing-host.trx` and `tests/IPS.Middleware.IntegrationTests/TestResults/outgoing-host.trx`.

Test-only process probes use isolated generated LocalDB databases and local HTTP/independently Java-signed protocol fixtures. Production cutover and live IPS/CBS interoperability are outside this evidence.

## Standards

Independent review initially found a malformed collection mapping issue and a disposal/lifecycle guard omission. Both are fixed with regressions. Final recheck: zero remaining actionable findings.

## Spec

Independent review found unbounded SQL status observation and null structured-remittance mapping. HTTP-budget cancellation now covers observation and has a committed fallback; invalid list entries reach Application validation while duplicates still bypass new-request checks. Final recheck: zero remaining findings.

## Limits and next gate

Defaults remain disabled. Connection limits apply per instance; no performance claim is made. SQL leases use existing staging-time expiry/rowversion protection. pacs.028 and protocol-authorized resend remain unimplemented and must be specified next. Owner approved committing this increment on 2026-10-05. Owner approval is still required before merging.
