# PR 1a: transaction state and history

Status: Ready for owner review; not merged.

Repository: the new `IPS.Middleware` repository. Branch: `codex/transaction-lifecycle`. Candidate implementation: `1186370`. Base: foundation `705eeb3`. No remote is configured. Both the foundation and this capability remain unmerged; no work has been integrated into the original repository.

## Result

The old transaction entity combines status/history with database identity, tracing, retry scheduling, and callback delivery. This PR rebuilds state and history as an isolated Domain model, with immutable observations and a protected history collection. The current observation is the same entry as the recorded status change, so callers cannot create conflicting copies of current and historical details.

The source behavior is preserved: UTC timestamps, normalized reasons, bounded descriptions, repeated observations, and final-state classification. Eligibility rules remain with the workflows; this model does not invent a universal transition graph.

Specification, source paths, acceptance scenarios, and recorded gaps: [001a-transaction-lifecycle.md](../specs/001a-transaction-lifecycle.md). Behavior reference: original commit `d498de6c4638aa71cdb20189d13642b41abab5f1`.

## Review the diff

From the new repository:

```powershell
git diff 705eeb3...codex/transaction-lifecycle
```

Only Domain and its tests gain implementation. The test project gains an explicit Domain reference. Documentation records the scope and terms. No production dependency or package, Contracts change, HTTP endpoint, worker, or storage adapter is introduced.

## Verification

- Release build: 0 warnings, 0 errors.
- Tests: 58 passed, 0 failed, 0 skipped (30 lifecycle, 21 architecture/compatibility, 7 host).
- Formatting and diff whitespace: verified.
- Lifecycle cases cover normalization, history protection, current/history consistency, repeated observations, final classification, preserved final-state replacement behavior, and validation before mutation.

## Standards

Documented-standard violations: None found. The change stays inside the specified Domain capability, keeps production dependency direction intact, introduces no packages or infrastructure concerns, and leaves Contracts unchanged. Tests exercise the public model's behavior, including immutable history, normalization, and failed-operation atomicity.

Smell heuristics: No actionable findings. PaymentTransaction owns append order and current-state selection; TransactionHistoryEntry owns observation normalization. The shared append method consolidates mutation without adding an abstraction. Supplied timestamps avoid ambient-clock coupling. Workflow eligibility and persistence remain explicitly deferred rather than appearing as unused hooks.

## Spec

No findings. The implementation matches all eight specified behaviors: normalized creation, immutable status/step history, UTC timestamps with explicit append order, consistent explanation normalization, cleared replacement details, repeated observations, correct final classification, permissive transitions, and validation before mutation.

Compared with pinned d498de6, the rejection scenario and original entity semantics are preserved. The encapsulation improvements and deferred persistence/workflow concerns are explicitly documented. Tests cover the acceptance scenarios, and the change adds no endpoints, workers, adapters, packages, or Contracts modifications.

Total findings: Standards 0; Spec 0. Both reviews were independent and read-only against `705eeb3...1186370`.

## Remaining work and approval

Capability 1b remains outstanding: atomic durable intake, duplicate handling, SQL schema/migrations, concurrency, claims, and restart recovery. This PR makes no database durability or multi-instance guarantee.

Owner approval is required before merging each branch. Any approved merge goes into main only in the rebuilt repository. Switching over from the original project is a separate owner decision after the rebuild is satisfactory.
