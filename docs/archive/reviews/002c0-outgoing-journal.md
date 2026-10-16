# Review 2c.0 — Explicit outgoing message journal

Branch: codex/outgoing-http. Base: c7c38b9. Owner authorized implementation of the adapted outbound design on 2026-10-05. Owner approved this review for commit and merge on 2026-10-05. Original repository, main and other review branches are preserved.

## Delivered

OutgoingMessages separates technical messages from OutgoingPayment business state. The exact selected signed/development-unsigned wire message commits as ReadyToSend, then SendStarted commits before external I/O. Raw response evidence commits independently as Received. Interpretation commits Processed or Failed with aggregate outcome/events and ownership release. A valid business rejection is Processed; invalid/untrusted/inconclusive evidence leaves the payment Uncertain. The outbound record retains its historical SendStarted state.

Stable request snapshots, identifiers and unsigned preparation remain on Transactions. SignedXml, SubmissionJson and SubmissionResponseJson are removed from the current payment model. Existing preparation/submission repositories now use the journal; immutable projections expose complete correlated evidence. A frozen development-unsigned message cannot be silently replaced when a certificate later becomes available. Current signing policy still controls whether it may be sent.

The existing committed payment claim and parent rowversion fence journal writes. The journal interceptor validates exact authorized values and defers row writes until after the parent UPDATE, in the unchanged unit of work's second phase alongside events. Both phases are one transaction. This prevents stale competing writers from encountering child uniqueness before parent concurrency. Failed contexts are disposed. Incoming storage, Domain enums/events, Contracts and HTTP routes are unchanged.

## Migration

Official command: `dotnet ef migrations add OutgoingMessageJournal --project src/IPS.Middleware.Infrastructure --context TransactionDbContext --output-dir Transactions/Migrations --configuration Release`.

Generated artifacts: 20261005121900_OutgoingMessageJournal.cs, its designer, and TransactionDbContextModelSnapshot.cs. Generated Up/Down reviewed: drops the three obsolete columns, replaces the preparation constraint, creates OutgoingMessages with initial-message uniqueness, same-payment response foreign key and lifecycle/null constraints. No unrelated schema change. The new uncommitted migration was regenerated through the CLI after tightening SQL null handling; no generated migration or snapshot was hand-authored. Design-time configuration has no database connection. Historical migration files remain unchanged.

This is destructive to the obsolete columns and intentionally contains no history conversion. Complete migration chain is supported only for fresh databases. Tests migrated isolated generated LocalDB databases; no application or production database was migrated.

## Verification

- Release build: zero warnings/errors.
- Full final suite: **734 passed, zero failures/skips** (249 unit/architecture/Contracts + 485 integration).
- Focused journal/preparation/submission/workflow suite: **84 passed**.
- Formatting verification, git whitespace check and EF pending-model check: pass.
- Real SQL coverage: exact evidence and header round trip, competing writers, expired/stale ownership, rollback/cancellation, one-way immutable checkpoints, explicit unsigned disposition, SQL uniqueness/correlation/null constraints, saved-response replay and interpretation rollback.
- Outgoing restart tests simulate interruption at durable boundaries using failed commits and fresh SQL scopes; the independent IPS simulator verifies a lost reply does not cause another original send. Existing incoming real-process termination, multi-host HTTP and enabled-Api smoke tests also pass in the full suite. Outgoing process-host activation remains the later HTTP review.

The first full run exposed an existing time-dependent incoming fixture: certificates were generated relative to wall-clock time while the workflow clock was fixed at 2026-10-04. The certificate helper now accepts an explicit validity anchor and the incoming fixture supplies its fixed date. Production certificate validation is unchanged. After this test-only correction, the complete suite passed. The independent Spec reviewer also checked that correction.

## Standards

Zero remaining findings. One documentation finding was fixed: architecture sections now explicitly supersede the old shadow-column storage and describe journal ownership and parent-first persistence. No hard documented code violation or actionable abstraction/duplication concern remained.

## Spec

Zero findings. Independently reviewed immutable evidence, commit boundaries, parent-first rowversion fencing, response replay, development unsigned policy and scope. No endpoint/callback/worker/pacs.028 implementation was introduced.

## Next gate

Owner approval to commit and merge was received on 2026-10-05. After approval, durable callback/status-read implementation follows, then supervised HTTP/host integration, then pacs.028 investigation and safe resend policy. Live processing remains opt-in and disabled by default. The first journal schema intentionally supports initial pacs.008 and its response only; later message families extend it in their own review.
