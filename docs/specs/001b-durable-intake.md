# Capability 1b: durable outgoing intake and transaction updates

Status: Implementation and SQL verification in progress; owner review pending.

Branch: `codex/durable-intake`, based on lifecycle commit `d2ed3ac`. Earlier branches remain unmerged. All changes and Git operations belong to the rebuilt repository.

## Commit evidence

These commits in the original repository explain how this behavior evolved. The final reference remains `d498de6c4638aa71cdb20189d13642b41abab5f1`; historical patches are evidence rather than changes to replay mechanically.

| Source commit | Relevant change | Rebuild treatment |
|---|---|---|
| `96b99f0` | Introduced transaction state and history | Rebuilt in 1a |
| `0202969` | Introduced outgoing intake and SQL transaction store | Atomic intake and optimistic updates in 1b; dispatch/recovery later |
| `7006e4f` | Extended lifecycle for core status reconciliation | Vocabulary retained in 1a; incoming workflow remains capability 4 |
| `5e1e748` | Prioritized pacs.008 queries using two database queries | Pending-work selection belongs to 1c |
| `251781d` | Added telemetry to transaction persistence | No Domain telemetry coupling; diagnostics will be introduced in Infrastructure with operational workflows |
| `8b9f38c` | Moved request JSON to document storage while keeping atomic intake | Keep atomic request/transaction/history persistence with a fresh schema; XML documents arrive with the XML capability |

Pinned source paths:

- `IPS.MiidleWear.API/Transactions/OutgoingTransactionIntake.cs`: normalized client reference; return an existing transaction before creating another; persist request before returning; return the winner after an insert race.
- `IPS.MiidleWear.Persistence/Transactions/PaymentTransactionStore.cs`: transactional SaveChanges, unique-reference handling, row-version updates, and request retrieval.
- `IPS.MiidleWear.Persistence/Configurations/PaymentTransactionConfiguration.cs`: a unique client reference across message types, SQL rowversion, and field lengths.
- `IPS.MiidleWear.Tests/Api/OutgoingTransactionFlowTests.cs`: durable Processing intake and duplicate submissions. Existing tests use an in-memory store and do not establish SQL atomicity or concurrency.
- `IPS.MiidleWear.Tests/Domain/WriteReliabilityTests.cs`: evidence that durable receipt precedes later processing; its incoming-specific workflow is deferred.

## Behavior and acceptance

1. Application accepts a validated message type, client reference, and serialized request. It creates an outgoing Received transaction using the supplied clock and returns only after storage completes. Blank references/payloads and cancellation are rejected before persistence. Serialization and message-specific validation remain at the future HTTP/protocol edges.
2. Transaction, original request text, and initial history commit together. A failed history insert leaves none of them stored.
3. Client reference is unique across message types. Concurrent submissions return one winner. A repeated reference returns the original message type, current outcome, and identity, without replacing the original payload, even when the repeated payload differs.
4. SQL's database collation governs reference comparison, as in the source. No new case-sensitivity or conflict-on-different-payload policy is introduced. Deployment collation is an unresolved environment requirement to characterize before cutover.
5. A duplicate-key exception is considered idempotency only when the requested client reference actually exists. An unrelated primary-key collision remains an error rather than a null result or false success.
6. Each read returns a coherent current status and ordered history. A new store/context can load a committed transaction and the exact original request. Missing identities return null.
7. Application supplies a synchronous update decision that runs once and has no external side effects. The store reports Saved, NotFound, Unchanged, or Conflict explicitly. Declined/no-op decisions do not write; thrown decisions propagate without writes.
8. Concurrent decisions based on the same row version cannot both commit. The winner's current state and appended observations persist together; the loser returns Conflict. Callers decide whether to reload/retry. The storage adapter never replays a decision or remote side effect automatically.
9. Status updates and appended history share one SQL transaction. The parent row is updated with a row-version check before inserting new history. If history insertion fails, the parent update rolls back too. Step-only changes also advance the parent version through the last history sequence.
10. Rehydration preserves append order, equal/backward timestamps, normalized reasons, and the distinction between status changes and technical observations. Inconsistent stored history is rejected instead of silently producing a different current outcome.

## Design

Application owns a small transaction-specific storage interface, intake, and explicit results. Infrastructure owns EF records, mapping, transactions, and SQL constraints. Domain is unchanged and remains dependency-free. There is no generic repository, public query expression, event bus, or new production host.

The fresh schema contains Transactions (including the original JSON, current-state fields, and rowversion) and TransactionHistory (keyed by transaction identity plus append sequence). Current details are rebuilt from immutable history. The adapter verifies that the recorded current state agrees with that history.

Consistent reads use a short repeatable-read transaction: read and lock the parent first, then load its history while that lock prevents an adapter writer from changing the parent and appending history. Read locks are released before an update decision runs; a subsequent write transaction checks the original rowversion. This avoids holding read locks across caller work while detecting changes between read and write. Full-history loading is intentionally simple; its performance is not established for long-lived transactions.

EF Core's documented [transaction guarantees](https://learn.microsoft.com/en-us/ef/core/saving/transactions) and [SQL Server concurrency tokens](https://learn.microsoft.com/en-us/ef/core/saving/concurrency) inform the implementation. Real SQL tests, rather than an in-memory provider, verify the guarantees above.

## Verification and scope

Use the repository-local EF CLI to generate InitialTransactionStorage in Infrastructure. Check the generated Up/Down, apply/rollback/reapply it in disposable SQL Server LocalDB databases, and check that the model has no pending changes. Test databases have fresh generated names beginning IPS_Middleware_Tests_; cleanup verifies the exact database and local server before deletion.

The host still exposes only liveness and development OpenAPI and does not connect to storage. No migration is applied to an existing application database. No API status meanings or Contracts declarations change.

Application tests cover waiting for persistence, failures, cancellation, and returning the store's existing outcome. SQL tests cover durable request reload, repeated cross-type references, simultaneous inserts and updates, unrelated key collisions, database constraint failures, rollback, declined/thrown decisions, ordered history, generated migration round trips, and a controlled read/write overlap proving the parent read lock is held until history is loaded.

Remaining capability 1c: pending-work discovery, claims/leases, and restart recovery foundations. A new store/context proves persisted data can be reopened; process crashes, abandoned work, and multi-instance lease fencing are not yet verified. HTTP intake, wake-up signals, identifiers, payment validation, and dispatch belong to later capabilities.

Approved external behavior differences: none. The fresh schema and stronger internal consistency checks follow the approved internal redesign.
