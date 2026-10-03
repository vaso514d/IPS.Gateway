# IPS Middleware

A fresh foundation for rebuilding the IPS middleware in reviewed capability increments.

**Status:** foundation, transaction lifecycle, and durable intake are awaiting separate reviews. The executable exposes liveness and development OpenAPI. Payment operations and workers have not been implemented.

## Start

The full test suite requires Windows and SQL Server Express LocalDB (MSSQLLocalDB). Each SQL test creates and deletes its own database; no application connection string is used. Install the SDK selected by `global.json`, then run from the repository root:

```powershell
dotnet tool restore
dotnet restore IPS.Middleware.slnx
dotnet build IPS.Middleware.slnx --configuration Release --no-restore
sqllocaldb start MSSQLLocalDB
dotnet test IPS.Middleware.slnx --configuration Release --no-build
dotnet run --project src/IPS.Middleware.Api --launch-profile Local
```

The local profile listens on `http://localhost:5080`:

- `GET /health/live` returns `Healthy`.
- `GET /openapi/v1.json` describes the foundation endpoint in Development.
- The host requires no database, certificates, IPS connection, or core banking connection.

## Structure

`src/` contains Contracts, Domain, Application, Infrastructure, and the single Api host. `tests/` contains behavior/architecture/compatibility tests and host integration tests.

The existing `IPS.MiidleWear.Contracts` package and namespaces intentionally keep their original spelling for consumers. New internal projects use `IPS.Middleware`.

- [Architecture and dependency diagram](docs/architecture.md)
- [Contribution and review process](CONTRIBUTING.md)
- [Capability migration ledger](docs/migration-ledger.md)
- [Transaction lifecycle specification](docs/specs/001a-transaction-lifecycle.md)
- [Transaction lifecycle review](docs/reviews/001a-transaction-lifecycle.md)
- [Durable intake specification and source commits](docs/specs/001b-durable-intake.md)
- [Durable intake review](docs/reviews/001b-durable-intake.md)
- [Transaction terminology](CONTEXT.md)
- [Foundation review and verification](docs/foundation-review.md)
- [Contract baseline provenance](tests/IPS.Middleware.Tests/Baselines/README.md)

## Source and compatibility

The behavior reference is [IPS.MiidleWear at d498de6](https://github.com/vaso514d/IPS.MiidleWear/tree/d498de6c4638aa71cdb20189d13642b41abab5f1). The imported Contracts sources are byte-for-byte Git objects from that commit. The source repository's 233 passing tests are scenario evidence, not a claim that every documented behavior is correct.

All development and Git operations happen in this rebuilt repository. The original repository serves as a read-only behavior reference, and the owner decides when to switch over. This repository has independent Git history. Foundation work stays on `codex/foundation`; it will be merged only after the owner's approval. The lifecycle branch is temporarily based on the unmerged foundation; its review compares against that branch. Durable intake is stacked on the lifecycle branch. Each merge needs explicit owner approval. There is no remote yet.

Reporting, standalone generator/mock hosts, and document tools are outside the rebuild scope. Protocol simulators will be introduced inside the integration tests when their capability needs them.
