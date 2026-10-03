# Working in this repository

Keep development and Git operations in this rebuilt repository. The original repository is a read-only behavior reference; switching over requires an explicit owner request.

- Before changing production code or project references, read [the architecture](docs/architecture.md).
- Before continuing the rebuild, including after compaction or in a new session, read the [resume checkpoint and commit audit](docs/migration-ledger.md#resume-checkpoint--2026-10-03) and follow the [commit-by-commit workflow](CONTRIBUTING.md#commit-by-commit-rebuild-workflow). Account for original commits in order; use the capability backlog only to track scope.
- Use [the contribution process](CONTRIBUTING.md) for verification and review. Present the diff, test evidence, and unresolved findings; wait for the owner's approval before merging.
- Preserve the Contracts package identity and imported public interface. Read [baseline provenance](tests/IPS.Middleware.Tests/Baselines/README.md) before changing Contracts or compatibility expectations.
- Build only the current capability scope documented in the ledger and its specification. Add payment endpoints and workers only with their implemented capabilities; never add placeholder handlers.
- Keep payment decisions in Application, protocol/storage/scheduling in Infrastructure, and HTTP mapping in Api. Add concrete feature code before introducing a shared abstraction.
- For EF Core changes, generate migrations with the official EF CLI and verify the artifacts; never hand-write migrations or snapshots.
