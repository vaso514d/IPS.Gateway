# Capability migration ledger

## Resume checkpoint

Read this after compaction or in a new session, then follow the [capability-by-capability workflow](../CONTRIBUTING.md#capability-by-capability-rebuild-workflow). Keep this section to the current state: when a slice merges, replace the paragraph below rather than adding a dated one above it. Earlier checkpoints, slice records and the original capability backlog are in the [ledger history](archive/migration-ledger-history.md).

Active (2026-10-17): [019 the gateway's API document, Swagger UI and a typed gateway client](specs/019-gateway-api-document-and-client.md), on main at 19a9bb6 (017b and 018 merged through PR #3). The owner chose Swagger UI, exposure behind `Api:Documentation:Enabled` (always on in Development), a new `IPS.MiidleWear.Client` package, and working on main. On 2026-10-17 the owner approved the specification with the recommended decisions: D1, the client throws `IpsGatewayNotFoundException` on a 404 to the status query; D2, the packages move to `2.0.0-preview.2`; D3, Swagger UI "Try it out" in Development only. The owner then had 019a revised to generate the documents from code with Maverick-style annotations: `[GatewayOperation]` and typed examples on the controllers; the receiver document comes from `AddIpsCoreReceiverOpenApi` in the core system's application; no committed JSON, with files written on demand by `Microsoft.Extensions.ApiDescription.Server`. The revision is implemented, uncommitted. Next action: verify and review the revised 019a and present it for commit approval, then 019b and 019c.

Proposed (2026-10-19): [020 one table per message type and a transfer status history](specs/020-message-tables.md), on codex/uat-certificates; see its status line for the owner's decisions.

## Reference

Original source: [vaso514d/IPS.MiidleWear](https://github.com/vaso514d/IPS.MiidleWear/tree/d498de6c4638aa71cdb20189d13642b41abab5f1) at `d498de6c4638aa71cdb20189d13642b41abab5f1`, read-only. Its code and 233 passing tests (2026-10-03) are behavior evidence to inspect, not a substitute for protocol requirements.

Approved differences from the source: one executable host; new internal layering and names; a fresh database schema; no reporting or standalone helper hosts and tools; synchronous outgoing 200/504 and immediate duplicate 200 ([ADR 0002](adr/0002-contract-compatibility.md)).
