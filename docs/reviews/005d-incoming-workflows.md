# 005d: Incoming workflows and protocol

Base: 51986d0 on codex/readability-refactor. Implements increment 4 of 005; no merge authorized.

Reconciliation separates saved-response replay, the bounded remote call, closed/deadline handling and final scheduling. Per-run state retains committed outcomes and the reconciliation attempt count. Reply processing separates message preparation, replay/retry policy and one durable submission. Initial CBS processing retains its existing explicit phases with a descriptive per-attempt state type. The CBS wire DTO is a simple class with init properties; strict duplicate/correlation handling remains. The pacs.002 builder names its header, original group and transaction sections.

Reviewed and retained receipt intake, payment identity, composition and protocol mapping/validation where their existing code was already direct. No incoming state, deadline, ownership, reversal, acknowledgement or reply behavior is changed.

Independent Standards review: clear. Independent Spec review: clear, covering replay-before-expiry, frozen IPS decisions, reversal safety, committed results, cancellation, durable attempts, JSON strictness and XML order. Final verification: 876 tests pass (257 unit/architecture/Contracts, 619 integration), no failures/skips. Release build, formatting, EF model consistency and whitespace checks pass. Real LocalDB, crash/recovery, protocol and host tests are included. Results: readability-incoming.trx in both test projects.
