# Review 004c.3 — Incoming workers and recovery

Branch: codex/incoming-workers. Base: cede1c5, merged into codex/capability-rebuild with owner approval on 2026-10-05. The owner approved committing and merging this increment on 2026-10-05. Original repository and other branches are preserved.

## Delivered

Four Infrastructure hosted roles connect the existing incoming pacs.008 workflows: one outstanding receive call, concurrent processing dispatch, concurrent reply/retry dispatch, and independent CBS follow-up. Two bounded journal-ID channels remain notification mechanisms; the first committed SQL claim owns execution. The mistaken MassTransit/Azure directions were explicitly withdrawn before implementation. No broker or messaging package was added.

Receive commits before notification and retries a failed commit before polling again. Successful nonempty bodies are preserved, including whitespace and contradictory EMPTY metadata. Unsupported/missing message types enter the existing hold path. Processing and reply discovery are separate, ordered SQL queries sharing the existing due/ownership predicate. Stored unsent replies go directly to delivery recovery.

Processing capacity derives from IPS/CBS connection limits; CBS follow-up reserves capacity, and first replies/retries share admission before receipt claims. Tasks are tracked and awaited. Shutdown stops admission/receive, drains for the configured budget, then cancels and awaits bounded evidence persistence. Host stop is concurrent and its deadline includes the persistence allowance. Repeated stop/disposal is idempotent.

Api activation requires explicit Workers:Enabled plus complete transport, SQL and protocol settings. Disabled startup has no external dependencies. Persistence resolves the final configured connection string lazily. No automatic database migration, schema changes, Contracts changes, MessageAck, unsupported payment handlers, outgoing endpoints or production activation.

## Verification

Release build: zero warnings/errors. Full suite: **722 passed, zero failures/skips (249 unit/architecture/Contracts, 473 integration)**. After that run, one additional actual enabled-Api flow test was added and passed separately; total verified scenarios: **723**. All production code was covered by the full run. LocalDB was available; tests used isolated SQL databases, independent HTTP peers and Java JSR105 signatures. Final formatting and EF model consistency checks passed; no migration is needed.

New tests cover bounded parallel dispatch, draining/cancellation, repeated stop/disposal, shared reply admission, capacity/settings validation, receipt rollback without publication, empty/error polling, whitespace preservation, separate discovery and lost notifications. Two service hosts share SQL and HTTP peers: one CBS submission for canonical identity, distinct reply references, accurate duplicate counts, held unsupported receipts, and two durable reply attempts per receipt on failed delivery. No MessageAck is observed.

A test-only executable probe is built in IntegrationTests. Parent tests kill only their own child process after committed receipt, registration, CBS submission marker, saved CBS response, signed reply, reply-attempt marker, and saved reply response; another case kills after the HTTP peer receives a reply but before returning its response. Restart uses new channel contents and scopes. Tests verify no repeated original CBS submission, follow-up query/reversal independent of completed/held receipts, saved response replay, exact XML replay and preserved two-attempt budget. Reply-marker recovery has two durable attempts but one actual send; lost-reply recovery sends the same XML twice. The probe cannot run against a non-test/non-LocalDB connection.

The additional enabled-Api test exercises final configuration binding, startup certificate loading, database wiring, receipt/CBS/reply processing and liveness through WebApplicationFactory and an independent HTTP simulator. No live IPS/CBS service was contacted.

## Independent reviews

Standards: **zero remaining actionable findings**. Spec: **zero remaining findings**. Early findings fixed: preserve whitespace bodies, align host shutdown timeout, derive reply admission from one transport setting, and include a reply-attempt-marker process crash case. The full suite additionally exposed repeated stop/disposal and a test-clock race; both were corrected and regression-tested.

## Remaining gate

Owner commit/merge approval was received on 2026-10-05. Release build and formatting were rerun successfully before commit; the SQL evidence above remains the completed verification record. Live processing stays disabled. Actual IPS late-reply acceptance/retention and deployment certificate-store/ACL behavior remain the previously recorded pre-production verification gates. Test connection capacities are bounded settings, not throughput claims. Broker-free multi-instance operation relies on shared SQL. Source evidence and acceptance details: [specification](../specs/004c3-incoming-workers.md), [approved live plan](../specs/004c-live-incoming.md), [configuration](../configuration.md).
