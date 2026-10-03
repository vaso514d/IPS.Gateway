# Working in this repository

- Before changing production code or project references, read [the architecture](docs/architecture.md).
- Before implementing a capability, read [the migration ledger](docs/migration-ledger.md) and establish its behavior specification from the pinned source evidence.
- Use [the contribution process](CONTRIBUTING.md) for verification and review. Present the diff, test evidence, and unresolved findings; wait for the owner's approval before merging.
- Preserve the Contracts package identity and imported public interface. Read [baseline provenance](tests/IPS.Middleware.Tests/Baselines/README.md) before changing Contracts or compatibility expectations.
- Build only the current milestone. The foundation introduces no payment operations, placeholder handlers, or workers.
- Keep payment decisions in Application, protocol/storage/scheduling in Infrastructure, and HTTP mapping in Api. Add concrete feature code before introducing a shared abstraction.
- For EF Core changes, generate migrations with the official EF CLI and verify the artifacts; never hand-write migrations or snapshots.
