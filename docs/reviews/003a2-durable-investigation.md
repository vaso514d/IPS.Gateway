# Review 003a.2: durable outgoing investigation

Branch codex/outgoing-investigation-workflow, base 60988d9. Protocol commit merged with owner approval; the owner approved committing this increment. The commit containing this record is the implementation checkpoint; merge approval remains pending.

## Delivered

Callable Application workflow and explicit Infrastructure registration with separate recovery/processing scopes. Stable investigation identities, frozen deadlines, preparation checkpoints, selected XML, one-send markers and complete response evidence use the existing journal and shared unit of work. SQL claims and parent rowversion fence writes before evidence insertion; new feature save rules protect immutable metadata. Initial payment queries filter initial records explicitly and their uniqueness remains enforced.

Saved evidence is interpreted before expiry. Accepted/rejected outcomes commit atomically with aggregate events and existing callback obligations. Unknown reports and transport failures schedule another investigation cycle. Marker-only recovery never repeats that marked request. Verified not-found evidence becomes a durable obligation and is excluded from discovery; it does not resend the payment. Normal first delay, immediate eligibility after abandoned ownership recovery, separate cycle limits and exact deadline cutoff follow owner decisions. Settings are validated at startup without activating workers.

## Schema

Generated with official EF10.0.12 CLI using `dotnet ef migrations add <name> --project src/IPS.Middleware.Infrastructure --output-dir Transactions/Migrations --configuration Release`:

- 20261005163512_OutgoingInvestigations adds preparation/result metadata and investigation-correlated journal rows, filtered uniqueness and foreign keys.
- 20261005164050_FreezeInvestigationDeadlines adds the absolute deadline after review identified the configuration-change risk.

Historical migration files remain intact. Only fresh-database chains are supported. The generated deadline default is not a conversion for existing investigation data. Recreate disposable databases; downgrade with populated investigation journal rows is not supported. No migration was applied to an application or production database.

## Standards

Independent review found no actionable standards issues. A deadline configuration concern was raised and fixed with persisted identity metadata; recheck clear. Generic unit of work remains unchanged.

## Spec

Review found lazy settings registration was not validated during startup. Explicit startup resolution and invalid-settings regressions address it. Frozen deadline regression verifies configuration changes cannot extend a running investigation. Recheck: no remaining actionable findings.

## Verification

Final verification: 871 tests passed (254 unit/architecture/Contracts and 617 integration), with zero failures or skips. Release build has zero warnings/errors; formatting verification, EF migration/model consistency and whitespace checks pass. The full suite includes real LocalDB persistence/concurrency tests and host smoke checks. SQL workflow tests passed for signed accepted/rejected reports, 1016/1017, malformed evidence, retry/deadline boundaries, snapshot reuse, replay after expiry, marker-only recovery, competing/stale ownership and atomic rollback. Additional cases exercise immutable metadata, immediate recovery eligibility, explicit DI scopes, configuration validation and remaining-window cancellation.

A first full run exhausted the two-second evidence-save budget in two new SQL tests under parallel fixture startup. The test class now runs outside parallel fixture initialization while keeping real production budgets and its internal concurrent-owner test. Production timeout behavior was not loosened. This is correctness evidence, not throughput or deployment sizing evidence.

## Remaining

No automatic investigation worker, payment resend, incoming unsolicited pacs.002 handler, new endpoint or Contracts change. Protocol-authorized resend and runtime scheduling are next; unsolicited status reports require their own durable receipt/correlation/acknowledgement review. Real IPS interoperability remains unverified.
