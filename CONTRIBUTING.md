# Contribution and review

## Capability-by-capability rebuild workflow

This branch preserves the capability-led process requested by the owner on 2026-10-03. Apply it on codex/capability-rebuild and review branches intentionally created from it. Source commits are behavior evidence; chronological commit coverage is not the execution order here. The separate codex/durable-intake branch retains the commit-by-commit process.

1. Read the migration ledger checkpoint and architecture. Select the next incomplete capability slice recorded in the checkpoint.
2. Inspect relevant code, tests, documentation, and history at original reference d498de6c4638aa71cdb20189d13642b41abab5f1. Write a short behavior specification with acceptance scenarios, compatibility constraints, durability, and failure/retry behavior. Record contradictions explicitly; obtain the owner's decision before changing external behavior.
3. Reuse completed foundation, lifecycle, and durable intake/storage work. Implement one coherent slice within the allowed dependencies on a codex/<capability> review branch. Introduce endpoints, workers, typed clients, and XML mapping when that slice needs their implemented behavior.
4. Test through meaningful interfaces; use SQL Server for database concurrency and transaction guarantees. Run the verification commands below. Review correctness, unnecessary indirection, duplicated rules, oversized workflows, naming, comments, and coupling.
5. Present the diff, test results, source references, and remaining concerns. Wait for owner approval before merging. Local branches and reviewed diffs are the PR equivalent until a remote exists.
6. Update the ledger checkpoint before ending work or handing off: implemented slice, branch/base, checks, unresolved decisions, approval status, and exact next action. Read it again after compaction or in a new session.

Capability order: finish transaction/storage foundations (1c), outgoing pacs.008, outgoing reliability, incoming payments, pacs.009/pacs.004, recalls/payment initiation, proxy management, operational completion. Include essential reliability and diagnostics with each capability.

All development and Git operations belong to D:\vaso\Running\IPS\IPS.Middleware. The original D:\vaso\Running\IPS\IPS.MiidleWear remains a read-only reference. Record stacked branch bases explicitly; authorization to continue is separate from merge approval. This branch preserves an alternative workflow for later use and does not authorize implementation merely by being created.

## Verification

```powershell
dotnet tool restore
dotnet restore IPS.Middleware.slnx
dotnet build IPS.Middleware.slnx --configuration Release --no-restore
sqllocaldb start MSSQLLocalDB
dotnet test IPS.Middleware.slnx --configuration Release --no-build
dotnet format IPS.Middleware.slnx --verify-no-changes --no-restore --exclude src/IPS.MiidleWear.Contracts
dotnet ef migrations has-pending-model-changes --project src/IPS.Middleware.Infrastructure --configuration Release --no-build
git diff --check
```

Build warnings are errors. GitHub Actions repeats restore, Release build, tests, and formatting on Windows. It uploads test results and uses the runner's SQL Server LocalDB for isolated database tests. SQL tests create and delete only their own generated databases; they do not use application configuration. Branch protection will be configured when a remote is introduced.

## Migrations

Generate migrations with the repository-local EF tool in Infrastructure, using TransactionDbContext and output directory Transactions/Migrations. The design-time factory supplies model metadata without contacting a database. Review generated Up/Down and snapshot changes, then run the SQL integration tests. The Api host does not auto-migrate or connect to this database yet.

## Implementation conventions

- Group code by capability inside each layer; introduce folders when implementation exists.
- Workers manage polling, timing, cancellation, and scopes. Application decides what happens to a payment.
- Keep interfaces small and attached to real variation: storage, remote IPS/core/proxy systems, or another justified dependency with production and test implementations.
- Use concrete internal calls. Consolidate repeated behavior when two implemented capabilities demonstrate that it is the same rule.
- Keep error and retry decisions explicit. Inject `TimeProvider` when a workflow depends on time.
- Explain a non-obvious decision in a concise comment; put protocol evidence and acceptance criteria in the behavior specification.
- Keep environment-specific connection strings and certificate material outside tracked files.
- Contracts changes require a separate, approved compatibility decision. Formatting excludes the imported sources to preserve their exact bytes.

## Review evidence

Each review must account for every change against its specification and dependency rules. Present what the consumer observes, why it changed, how it was tested, and any limits of the evidence. Passing tests do not replace a protocol or architecture review.
