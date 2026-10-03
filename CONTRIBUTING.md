# Contribution and review

## Capability workflow

1. Branch from the last approved `main` using `codex/<capability>`.
2. Write a short behavior specification: input/output, persistence before acknowledgement, failure/retry behavior, compatibility constraints, and acceptance scenarios. Link the relevant ledger entry and pinned source files/tests.
3. Resolve contradictions between code, tests, and protocol documentation explicitly. Record proposed differences and obtain the owner's decision before changing external behavior.
4. Implement one coherent capability through the architecture's allowed references. Add tests through the interfaces callers use; use real SQL Server for database concurrency guarantees.
5. Verify the commands below. Review behavior and architecture against the specification, including unnecessary indirection, duplication, oversized workflows, naming, comments, and coupling.
6. Update the ledger and present the diff, tests, source evidence, and unresolved concerns.
7. Wait for owner approval before merging. Local branches and diffs are the review equivalent until a remote exists.

The first branch, `codex/foundation`, contains only the foundation described in the ledger. Later capabilities start after its approval.

## Verification

```powershell
dotnet restore IPS.Middleware.slnx
dotnet build IPS.Middleware.slnx --configuration Release --no-restore
dotnet test IPS.Middleware.slnx --configuration Release --no-build
dotnet format IPS.Middleware.slnx --verify-no-changes --no-restore --exclude src/IPS.MiidleWear.Contracts
git diff --check
```

Build warnings are errors. GitHub Actions repeats restore, Release build, tests, and formatting on Windows. It uploads test results and requires no live services for this milestone. Branch protection will be configured when a remote is introduced.

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
