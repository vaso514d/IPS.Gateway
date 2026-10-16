# Incoming foundations: durable receipt and recoverable scheduling

Approved by the owner on 2026-10-04. Review branch codex/inbound-foundations, base 5348f78 on codex/capability-rebuild. Incoming work now precedes outgoing Stage 2c. No commit or merge is authorized by implementation approval.

## Source and decisions

Original read-only reference d498de6c4638aa71cdb20189d13642b41abab5f1: Gateway/Services/SqlInboundMessageDeduplicator.cs stores receipt and XML together and deduplicates by ParticipantBic + Sequence using a unique index; Persistence/Configurations/InboundMessageReceiptConfiguration.cs defines that index; Tests/Domain/InboundMessageIdempotencyTests.cs covers receipt/duplicate metadata; Tests/Gateway/InboundAckOrderingTests.cs establishes receipt before acknowledgement, no MessageAck for pacs.008. Prefix source paths with IPS.MiidleWear. The owner's inbound-payment-flow.md supplies the receive/processing/response-pool design and ID-only channels backed by SQL.

Approved difference: missing/nonpositive sequences are saved Held, retaining their supplied value, without business processing or automatic acknowledgement. The old implementation processed missing sequences without deduplication. BIC is trimmed and uppercased invariantly, then compared using SQL Latin1_General_100_BIN2; message types are trimmed and otherwise preserved. Raw XML is stored unchanged, including malformed/empty content. Only envelope storage requirements are validated; no XML or payment policy runs here.

## Storage and operations

InboundMessageJournal is independent of payment aggregates/events. Immutable fields: ID, participant BIC, nullable sequence, message type, XML, PossibleDuplicate, UTC receipt time. Mutable operational fields: Pending/Processed/Held, hold reason, due time, claim token/expiry, rowversion, duplicate count/last duplicate time. Only positive sequences participate in the unique participant/sequence index. Duplicates retain the original payload, state, due time and ownership; PossibleDuplicate never decides uniqueness. Sequence remains authoritative even if a duplicate supplies different content. This does not yet establish business-payment deduplication across different sequences.

Application owns immutable input/results and concrete intake/work operations; repository interfaces are under Abstractions/Inbound. Infrastructure owns EF entities, configurations, repositories and scheduling. All staged mutations use the existing scoped context and shared unit of work. Receipt insert/increment and claim/update commits are rowversion protected. Failed scopes must be disposed. The Infrastructure registration entrypoint retries uniqueness/concurrency conflicts in fresh scopes, at most eight attempts; exhaustion propagates without a success or notification. Only a committed new Pending receipt is immediately notified; duplicates never notify. Retry preserves the original receipt timestamp and raw input.

Claims are staged then explicitly committed before execution. Acquisition requires Pending, due and no live claim. Completion and release require the exact current live token. Expiry is checked at staging time, matching existing ownership semantics; competing writes are fenced by rowversion at commit. Expired claims can be replaced without changing receipt data. Reacquisition permits durable workflow resumption, never blind repetition of future remote effects. Completed and Held rows cannot be acquired. Completion means journal processing is complete, not payment acceptance.

## Scheduling

One Infrastructure bounded Channel<Guid>, FIFO, capacity 256. Notifications never block or perform remote I/O. Full/cancelled notification leaves committed SQL work recoverable. Local duplicate queue IDs are coalesced while queued; SQL ownership remains authoritative across nodes and after dequeue. Discovery orders Pending/due/not-live-claimed rows by next-action time, received time and SQL GUID order. A single refill reads at most 100 IDs in a fresh scope; it never acquires them. Defaults: discovery interval 1 s, ownership 45 s. All values positive, batch <= capacity. No worker or host registration starts automatically. The interval is exposed for the later worker, not an inactive timer.

## Acceptance and boundaries

Test atomic XML/receipt storage, concurrent duplicate registration/counters, participant isolation, advisory duplicate flag, held invalid sequences, immutable originals, competing/stale/expired claims, cancellation, cross-feature rollback, queue saturation/coalescing, lost notifications/restarted queues, due-time ordering and exclusions. Generate migration with official EF CLI; check build/format/architecture/Contracts/model and real SQL tests. Report LocalDB failures as blocked verification.

No incoming payment aggregate, parser, IPS/CBS client, dispatcher, response channel, acknowledgement, host worker, endpoint or Contracts change. Incoming pacs.008 processing and then live three-pool integration are separate reviews. Unknown CBS results, business deduplication, acknowledgement ordering and immutable response replay require their own specifications. Independent Standards/Spec review and owner approval precede merge.
