# Contribution and review

## Commit-by-commit rebuild workflow

The owner's instruction on 2026-10-03 sets the execution order: walk the original repository history commit by commit. The capability backlog is a scope checklist, not permission to skip ahead.

1. Read the migration ledger's resume checkpoint and inspect the original history through pinned commit d498de6c4638aa71cdb20189d13642b41abab5f1. Establish and record the ordered history traversal before marking commits covered; account for side-branch changes and merge commits explicitly.
2. Take the next uncovered original commit. Explain what it introduced and why, using its diff, tests, and relevant documentation.
3. Compare it with the clean repository. Record each relevant change as already covered (with evidence), to implement, excluded (with a scope reason), or deferred (with a reason and a named follow-up). A commit is accounted for only when every relevant change has a disposition.
4. Write a short behavior specification covering acceptance scenarios, compatibility, persistence, and failure/retry behavior. Reimplement the required behavior in a focused codex/<change> review branch with the cleaner architecture and allowed dependencies. Historical commits provide evidence; do not mechanically cherry-pick them. Preserve external behavior and obtain an owner decision for proposed differences.
5. Test through meaningful interfaces, using real SQL Server for database concurrency guarantees. Run the verification commands below and review functional correctness, unnecessary indirection, duplication, oversized workflows, naming, comments, and coupling. Present source references, test results, exclusions/deferrals, and remaining concerns. Wait for explicit owner approval before merging into the copy repository's main.
6. Update the ledger's commit record and resume checkpoint before ending the session or handing off. Record the original SHA, disposition, rebuild commit/specification, checks, review/merge status, and exact next action. Resume from this record after context compaction or in a new session.

Keep implementation and Git changes in D:\vaso\Running\IPS\IPS.Middleware. The original D:\vaso\Running\IPS\IPS.MiidleWear is a read-only reference. Continue on explicitly recorded stacked branches when approved work proceeds before merges; record the base and keep merge authorization separate from authorization to continue.

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
