# Foundation review

Status: **Ready for owner review; not merged.**

Branch: `codex/foundation`. Base: `main`. No remote is configured.

## Result

The new repository contains five production projects and two test projects, one executable host, and the approved inward dependency graph. Transitive project references are disabled and evaluated dependency manifests are tested.

The three internal libraries contain no placeholder code. The host exposes only liveness and development OpenAPI. No business feature or external-service connection has been introduced.

Contracts were imported from raw blobs at `d498de6c4638aa71cdb20189d13642b41abab5f1`. Their sources/project file, 66 public types with route metadata, and 15 representative serialization cases are preserved.

## Verification

- Normal Release build: 0 warnings, 0 errors.
- Tests: 28 passed, 0 failed, 0 skipped (21 architecture/contract cases; 7 HTTP host cases).
- Formatting: verified, excluding imported Contracts.
- Real Kestrel host: startup, `/health/live`, and development OpenAPI verified.
- Local package inspection: original Contracts package/version, net8.0 assembly, and no package dependencies verified. Nothing was published.
- Fresh checkout: restore, Release build, all 28 tests, and formatting pass.
- Dependency guard probe: an intentionally added Infrastructure DLL reference in a temporary Domain project causes exactly the expected architecture-test failure. The real repository was not modified by the probe.

These checks prove the foundation and compatibility declarations. They do not certify payment behavior that has not been rebuilt.

## Standards

No remaining findings. The initial advisory identified a direct-DLL reference gap; resolved non-framework references are now recorded and rejected in core projects, and the negative probe verifies this enforcement. No current dependency violation or actionable code smell remains.

## Spec

No remaining findings. The initial finding identified an incomplete pacs.004 JSON sample; it now captures the actual nested original-payment reference and return reason from the independently built original assembly. All sample inputs reject unknown fields to prevent silently lost coverage.

Total remaining findings: Standards 0; Spec 0.

## Test project simplification

At the owner's request, Contracts compatibility tests use the existing project reference without linked source files or a source-byte identity test. Frozen public-interface, route metadata, and JSON expectations remain enforced. Generated architecture reports remain test inputs and are hidden from Solution Explorer. The source manifest remains as import provenance only.

## Owner review

The full review diff is `git diff main...codex/foundation`. The architecture rules are in [architecture.md](architecture.md), and later capabilities are in [migration-ledger.md](migration-ledger.md).

Owner merge approval is pending. The next capability is transaction lifecycle and durable storage, after foundation approval.
