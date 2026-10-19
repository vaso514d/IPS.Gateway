# IPS Middleware

A fresh foundation for rebuilding the IPS middleware in reviewed capability increments.

**Status:** incoming pacs.008 processing and synchronous outgoing pacs.008 send/status/callbacks are implemented behind explicit configuration. The 006 clean-code rewrite is under review on `codex/readability-refactor`; the callable outgoing investigation workflow still needs host integration in a later capability. Live processing is disabled by default. See the [resume checkpoint](docs/migration-ledger.md) for verification and merge status.

## Start

The full test suite requires Windows and SQL Server Express LocalDB (MSSQLLocalDB). The aggregate schema replaces legacy history; recreate disposable rebuild databases rather than upgrading existing data. Each SQL test creates and deletes its own database; no application connection string is used. Install the SDK selected by `global.json`, then run from the repository root:

```powershell
dotnet tool restore
dotnet restore IPS.Middleware.slnx
dotnet build IPS.Middleware.slnx --configuration Release --no-restore
sqllocaldb start MSSQLLocalDB
dotnet test IPS.Middleware.slnx --configuration Release --no-build
dotnet run --project src/IPS.Middleware.Api --launch-profile Uat
```

The `Uat` profile listens on `http://localhost:5080` and connects to the IPS UAT (see [local environment](docs/local-environment.md#connect-to-the-ips-uat)):

- `GET /health/live` returns `Healthy`.
- `GET /openapi/v1.json` describes the foundation endpoint in Development.
- Run outside the profile, with incoming workers and outgoing execution/transport disabled (the `appsettings.json` default), the host requires no database, certificates, IPS connection, or core banking connection.
- Opt-in incoming processing requires the explicit [worker configuration](docs/configuration.md#incoming-workers).
- Enabled outgoing execution exposes the existing pacs.008 send and transaction-status routes. Final or duplicate status returns 200; an unresolved initial request returns 504 after the configured wait.

## Structure

`src/` contains Contracts, Domain, Application, Infrastructure, and the single Api host. `tests/` contains behavior/architecture/compatibility tests and host integration tests.

The existing `IPS.MiidleWear.Contracts` package and namespaces intentionally keep their original spelling for consumers. New internal projects use `IPS.Middleware`.

- [Architecture and dependency diagram](docs/architecture.md)
- [Coding conventions](docs/coding-style.md)
- [Contribution and review process](CONTRIBUTING.md)
- [Capability migration ledger](docs/migration-ledger.md)
- [Transaction terminology](CONTEXT.md)
- [Active specifications](docs/specs) and [history: implemented specifications, reviews and plans](docs/archive/README.md)
- [Contract baseline provenance](tests/IPS.Middleware.Tests/Baselines/README.md)

## Source and compatibility

The behavior reference is [IPS.MiidleWear at d498de6](https://github.com/vaso514d/IPS.MiidleWear/tree/d498de6c4638aa71cdb20189d13642b41abab5f1). The imported Contracts sources are byte-for-byte Git objects from that commit. The source repository's 233 passing tests are scenario evidence, not a claim that every documented behavior is correct.

All development and Git operations happen in this rebuilt repository. The original repository is a read-only behavior reference; the owner decides when to switch over. Reviewed capability work is collected on `codex/capability-rebuild`. Each merge requires owner approval; the cleanup branch remains separate until approved. There is no remote.

Reporting, standalone generator/mock hosts, and document tools are outside the rebuild scope. Independent protocol and HTTP simulators belong to the integration tests.

## Signing preparation

`Payments:Signing:AllowUnsignedInDevelopment` defaults to false. Only an explicitly enabled value in the Development environment allows missing-certificate unsigned preparation. Enabling it in any other environment prevents startup. A supplied unusable certificate always fails; it never triggers unsigned fallback. Configured signing and trust certificates load at startup; rotation requires restart. Payment execution and transport must also be explicitly enabled with valid configuration.

Signing integration tests sign and verify in process; no Java or other runtime is needed. Test certificates are ephemeral; no real certificates are required.

Runtime settings and override examples: [configuration](docs/configuration.md).
