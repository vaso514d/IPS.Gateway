# 005a: explicit models and Domain readability

Branch codex/readability-refactor, base077470b. First increment of the owner-approved 005 rewrite; later increments remain pending. No merge is authorized.

## Changes

Converted 99 authored record declarations to explicit classes using compiler-resolved symbols, including named constructor arguments and copy expressions. Retained explicit equality where used by payment identity, immutable references/decisions, reversal notification, callback keys and header values. Constructor binding and serialized property names remain unchanged. Snapshot defensive copies remain in place. Data-only round-trip assertions now compare every public member using strict equivalence; functional assertions remain intact.

Added callback-key hash/coalescing, frozen nested payment comparison/copy, and existing event JSON regressions. Introduced coding conventions and a complete authored-file inventory. Applied braced control flow and one-operation-per-line formatting while preserving compact auto-properties and multiline mappings/transitions. Contracts and historical/generated migrations are unchanged.

## Verification

Baseline:871 tests. Current:874 tests pass (257 unit/architecture/Contracts and617 integration including real LocalDB, host and crash checks), zero failures/skips. Release build zero warnings/errors. Final formatting, whitespace and migration/model checks pass after layout-only corrections. Evidence: readability-models.trx in each test project's TestResults.

Initial verification exposed assertions relying on record-generated equality; fixed with explicit production value semantics for business use and strict complete-value assertions for transport/storage projections. No expectations or business rules were relaxed.

## Independent reviews

Standards:clear after correcting conversion-induced flattened expressions.
Spec:clear; equality, immutable snapshots, JSON and business behavior retained. Both final rechecks cover this increment only.

## Next

Increment2: typed persistence metadata, repositories and save rules, preserving shared-row concurrency and atomic writes. Capability development remains paused.
