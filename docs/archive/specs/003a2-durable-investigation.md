# Review 003a.2: durable outgoing investigation

Status: implementation specification prepared on codex/outgoing-investigation-workflow, base 60988d9. Owner approved committing and then merging protocol review 003a.1 into codex/capability-rebuild on 2026-10-05. All three policy decisions below are owner-approved. Implemented for review; verification results are recorded in the review evidence.

## Scope

Add a callable Application investigation workflow for outgoing pacs.008. Persist preparation, send markers, complete responses, interpretation and retry scheduling through the existing shared SQL context and unit of work. Reuse the reviewed protocol builder, signer and interpreter. Keep automatic resend, runtime worker activation and unsolicited incoming pacs.002 processing in separate reviews. Existing send/status routes, Contracts and callback rules remain unchanged.

## Source evidence and decisions

Pinned source d498de6c4638aa71cdb20189d13642b41abab5f1:

- API/Transactions/OutgoingTransactionDispatcher.cs records Uncertain and schedules now + FirstInvestigationDelay (default nine seconds).
- API/Transactions/TransactionRecovery.cs releases abandoned in-flight work as Uncertain due immediately. RecoverAsync counts a recovery cycle before investigation, but the immediate resend increments only ResendCount.
- Application/Options/IpsTransactionOptions.cs documents MaxAttempts as investigations plus resends, which conflicts with the code. Defaults: MaxAttempts zero (window-limited), MaxResends three, subsequent investigation delays 30 seconds, one minute, five minutes, then every 15 minutes. Window: 24 hours from original acceptance, falling back to creation in legacy source.
- Source expiry uses now - reference > window. No new remote call can have a positive remaining budget at exact equality; the proposed explicit rule is to stop at equality while replaying saved evidence first.
- Tests/Api/TransactionRecoveryTests.cs supplies scenarios for stuck ownership, accepted/rejected reports, 1016, 1017 and exhaustion.

Project prefixes are IPS.MiidleWear. The protocol slice already restricts exact message definitions, trust and correlation. Annex D citations in source comments do not establish live interoperability independently.

Owner decisions (2026-10-05):

1. Approved: preserve distinct source first-delay paths. Normal uncertainty schedules the first inquiry nine seconds after uncertainty is recorded; abandoned work becomes eligible immediately after committed ownership recovery, without another nine-second delay. Eligibility is subject to scheduler discovery, ownership acquisition and the investigation deadline.
2. Approved: preserve recovery-cycle and resend counters separately. Do not reset counters after restart.
3. Approved: stop new calls at the exact 24-hour deadline and require manual review when saved evidence cannot settle the payment. Replay saved evidence before applying expiry and clamp calls to remaining time.

## Durable model

Keep investigation preparation metadata separate from the payment's business state, alongside the explicit message journal. Each attempt has a stable identity, payment identity, ordered attempt number, request message ID, status-request ID UTC creation time and an immutable absolute deadline frozen on first investigation reservation. Later cycles reuse the same deadline even if configuration changes. Save those identities before protocol preparation; retrying certificate acquisition must not generate new identifiers.

Store exact selected send-ready XML, signing disposition, committed send marker and complete HTTP response in OutgoingMessages, correlated to the investigation attempt and original payment message. Do not duplicate authoritative response bodies or selected XML on the aggregate. Retain a preparation checkpoint if signing is deferred. Store transport-failure evidence separately when no HTTP response exists; never invent an HTTP status/body.

Extend uniqueness beyond the current one-message-per-payment/direction constraint while preserving exactly one initial pacs.008 message and its initial response. Each investigation permits one outbound record, one submission marker and one response. Initial preparation/submission queries must explicitly filter initial records; their current SingleOrDefault assumptions cannot range over the expanded journal.

Use immutable Application projections and feature-owned repository/protocol ports under Payments/Investigation; SQL repositories belong in Infrastructure/Repositories/Payments. Infrastructure entities and EF types remain internal. Parent rowversion is checked before appending investigation/journal evidence. Protect metadata and journal mutations against direct unauthorized edits using the existing save-policy pattern. The generic unit of work remains unchanged.

Generate the migration with the official EF CLI, preserving previous migrations. Fresh databases only; no conversion of disposable rebuild data or automatic host migration.

## Workflow

1. Load the tracked outgoing pacs.008 and durable latest investigation. Terminal payments and manual-review payments do not start investigations. A saved NotFound obligation is returned for the future resend workflow, without remote I/O.
2. Recover expired ownership using committed fencing, then acquire and commit a new claim in a fresh scope. Live owners and not-yet-due work cannot be displaced. Return committed facts only after concurrency conflicts; discard failed scopes.
3. Read saved evidence first. An unconsumed response is interpreted before checking deadlines, preparing another request or sending. A prior marker with no response records an abandoned/unknown investigation attempt and schedules the next cycle; never redispatch that marked request.
4. If no replayable evidence exists, apply the approved attempt/window limits. Exhaustion becomes ManualReview, clears automatic scheduling and creates the existing final-status callback obligation atomically.
5. Persist investigation identity and preparation checkpoint. Build from the accepted snapshot and original stable identifiers; use its frozen protocol settings. Sign under the existing current certificate/development policy. Certificate unavailability reschedules preparation without consuming a remote-send attempt or changing identifiers.
6. Persist ReadyToSend with exact selected XML. Persist SendStarted before calling the existing single-send IPS transport. No transaction spans remote I/O. Use the remaining window and configured call budget; ownership must exceed execution plus evidence-persistence budgets.
7. Persist the complete response before interpretation, using bounded service-owned evidence persistence after remote completion. Caller cancellation before dispatch makes no call; cancellation after dispatch leaves a recoverable marker/evidence. HTTP middleware supplies no retries or hedging.
8. Interpret through the reviewed pacs.028 protocol component. OriginalAccepted/OriginalRejected updates the aggregate from Investigating, preserves first-outcome semantics and atomically stores events, callback obligation, response consumption, scheduling and claim release. NotFound records a durable obligation tied to the exact verified response and releases ownership without a resend. Unresolved records evidence and schedules the next cycle according to the approved limits.

A completed NotFound obligation is excluded from investigation discovery. A not-found result must never become payment Rejected or NotSent. The later resend review must consume an authorization exactly once, within independently established protocol/time constraints; this review does not grant that authorization merely by returning an enum.

## Configuration and discovery

Bind and validate investigation settings through appsettings: first delay nine seconds, subsequent delays 30 seconds/one minute/five minutes, repeat 15 minutes, window 24 hours, cycle limit zero, discovery batch 50 and interval five seconds. Keep existing IPS call timeout 25 seconds, attempt budget 35 seconds, ownership 45 seconds and evidence budget two seconds. Reject invalid durations/counts and inconsistent timeout ordering. Preserve the separately configured initial 20-second submission window; it is not automatically a resend deadline.

Provide bounded SQL discovery for due uncertain/investigating work and expired investigation claims. Preserve payment priority, then oldest due work and deterministic ID ordering. Exclude terminal, manual-review, live-owned and not-found-obligation payments. Tests exercise discovery and the callable workflow; no hosted investigation scheduling is enabled yet.

## Verification

Use real SQL and independent protocol fixtures to prove:

- accepted/rejected outcomes commit with history and callback obligations;
- 1016 saves an obligation and makes no payment resend; 1017, AG09 ambiguity and invalid/untrusted evidence remain unresolved;
- exact retry boundaries, approved window equality, exhausted limits and immutable deadline anchor;
- preparation resumes with identical IDs/XML, with explicit development unsigned disposition;
- response replay precedes expiry and prevents additional sends;
- marker-only recovery schedules another investigation cycle without replaying the marked call;
- two owners cannot dispatch the same attempt, stale owners cannot save responses, and mixed state/history/scheduling changes roll back atomically;
- cancellation and failed commits retain recoverable evidence without pretending a remote response exists;
- multiple journal exchanges leave original payment preparation, response replay and status reads unchanged;
- SQL uniqueness, immutable records and full migration-chain/model consistency remain enforced.

Use a simulator that has already processed the original payment but loses its reply: investigation must resolve the payment without another pacs.008 submission. Run the full suite including existing process termination checks, build, formatting, architecture/Contracts, host smoke and migration consistency. Obtain independent Standards and Spec reviews. Present evidence and unresolved concerns; commit/merge only with owner approval.

## Following reviews

Protocol-authorized resend and supervised runtime integration; then durable unsolicited pacs.002 processing with separately established acknowledgement rules. These are required before claiming complete outgoing pacs.008 behavior parity. Real IPS/CBS interoperability and production activation remain outside this review.
