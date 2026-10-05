# 005: Middleware readability rewrite

Owner-approved plan, 2026-10-05. Branch `codex/readability-refactor`, base `077470b`. This branch includes the committed investigation increment; merging either branch still requires owner approval. Pause capability development until this rewrite is complete. Original repository and reference repositories remain read-only.

## Conventions and compatibility

Use explicit classes throughout authored Domain, Application, Infrastructure and Api code. Preserve immutable snapshots, domain events, reference comparison semantics, JSON names, stable event names/versions and existing business rules. Use direct feature handlers, typed Infrastructure metadata alongside directly mapped aggregates, thin API controllers, and focused EF CLI migrations where useful. Keep the dependency direction and shared unit of work. Do not import mediator/event-sourcing frameworks or change Contracts.

Use descriptive names, braced control flow, one operation per line and named methods for meaningful steps. Avoid large closures, generic workflow frameworks, pass-through interfaces, duplicate validation, and string-based EF metadata access in business/repository queries. Keep necessary ownership, immutable evidence, protocol validation and concurrency checks at their responsible boundary.

Reference examples inspected: DailyReports Application commands, Infrastructure repositories/unit of work/fluent configurations, Domain aggregates and API controllers; ListingSearchDataSync Application handlers, typed client and repository implementations. Their differing records and dependency conventions are not requirements: the owner chose classes, direct handlers and the middleware dependency direction.

## Reviewed increments

1. Models and Domain: replace records and with-expressions, preserve structural payment comparisons and snapshot round trips; clean Domain methods and document formatting conventions.
2. Persistence/repositories/unit of work: typed metadata, typed queries, explicit save rules and reduced context plumbing. Preserve the parent concurrency fence before dependent evidence/events and after-commit event acknowledgement.
3. Outgoing workflows/protocol: explicit intake, preparation, submission, investigation, status and callbacks; preserve all checkpoint and retry behavior.
4. Incoming workflows/protocol: receipts, registration, CBS, reconciliation, reversal, replies and composition; preserve canonical identity and separate ownership.
5. Clients/configuration/runtime: clear client, certificate, settings, supervised worker/admission/recovery and shutdown code with identical configuration keys and defaults.
6. API/completion: thin controllers preserving exact routes, JSON, 200/504, validation, acknowledgement and disabled-route behavior; audit every authored file and update architecture/checkpoint.

Finish verification and independent Standards/Spec review for each increment before the next. Focused commits on this branch are authorized by the implementation plan; no merge is authorized. No new payment capability or production activation.

## Verification

Baseline on 2026-10-05: clean Release build; 871 tests passed (254 unit/architecture/Contracts, 617 integration, no skips/failures), including real LocalDB and host tests. Evidence: readability-baseline.trx in each test project's TestResults.

For each increment run build, meaningful affected tests, full suite, formatting, architecture/Contracts and migration-model checks. Add focused equality/clone, historical JSON, EF materialization and controller binding regressions. Preserve acceptance assertions and independent protocol fixtures; test SQL ownership/rollback and restart behavior through meaningful interfaces. Record reviews and counts per increment, and maintain the file inventory.

## Completion

All six increments are implemented on codex/readability-refactor, each with full verification and independent Standards/Spec reviews. See [005f completion evidence and commit map](../reviews/005f-api-and-completion.md) and the [232-file inventory](../readability-inventory.md). Existing capability behavior and Contracts remain intact; new capability work resumes only after the owner's review/merge decision. The cleanup includes one official EF-generated index migration as documented in 005b.

On 2026-10-06 the owner requested recommitting after a branch reset. The six reviewed increments are now consolidated into one commit on the same branch, with the exact reviewed implementation preserved; the ledger and review retain verification provenance.
