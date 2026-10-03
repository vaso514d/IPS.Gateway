# IPS Middleware

A fresh foundation for rebuilding the IPS middleware in reviewed capability increments.

**Status:** foundation only, awaiting review. The executable exposes liveness and development OpenAPI. Payment operations and workers have not been implemented.

## Start

Install the SDK selected by `global.json`, then run from the repository root:

```powershell
dotnet restore IPS.Middleware.slnx
dotnet build IPS.Middleware.slnx --configuration Release --no-restore
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
- [Foundation review and verification](docs/foundation-review.md)
- [Contract baseline provenance](tests/IPS.Middleware.Tests/Baselines/README.md)

## Source and compatibility

The behavior reference is [IPS.MiidleWear at d498de6](https://github.com/vaso514d/IPS.MiidleWear/tree/d498de6c4638aa71cdb20189d13642b41abab5f1). The imported Contracts sources are byte-for-byte Git objects from that commit. The source repository's 233 passing tests are scenario evidence, not a claim that every documented behavior is correct.

This repository has independent Git history. Foundation work stays on `codex/foundation`; it will be merged only after the owner's approval. There is no remote yet.

Reporting, standalone generator/mock hosts, and document tools are outside the rebuild scope. Protocol simulators will be introduced inside the integration tests when their capability needs them.
