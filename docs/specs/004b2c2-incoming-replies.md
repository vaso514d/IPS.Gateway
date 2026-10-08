# 004b.2c.2 — Durable incoming reply artifacts and delivery

Draft on codex/incoming-replies, based on ac5e0bf. Owner approved and completed reconciliation merge into codex/capability-rebuild on 2026-10-05. Owner provisionally agreed to send the saved reply after the processing deadline, with the unresolved replay-window question retained below for future review. Owner also approved keeping completed duplicates completed without a new send. Implementation is authorized by the owner's continuation request; merge approval remains separate.

## Source evidence

Original reference d498de6c4638aa71cdb20189d13642b41abab5f1:

- IPS.MiidleWear.Gateway/Services/CoreApiInboundMessageHandler.cs: pacs.008 generates a pacs.002 reply after CBS handling; trusted structural failures return rejection without CBS submission.
- IPS.MiidleWear.Application/BackgroundServices/IpsInboundReceiverService.cs, SendPaymentReplyAsync: sends the same reply, defaults to two attempts with 200 ms delay, and logs critical undelivered status after exhaustion. Receipt audit distinguishes attempted from delivered.
- IPS.MiidleWear.Application/Options/IpsStpOptions.cs: PaymentReplyMaxAttempts and PaymentReplyRetryDelay.
- IPS.MiidleWear.Application/Transport/IpsHttpStpClient.cs and IpsStpSendResult.cs: ReplyToPayment uses the Message endpoint and returns RequestStatus/error/body evidence. The receiver's send loop treats a normally returned call as delivered; exact protocol success evidence must be characterized before copying this shortcut.
- IPS.MiidleWear.Tests/Gateway/InboundAckOrderingTests.cs: pacs.008 gets no MessageAck, and its pacs.002 follows processing.

Existing rebuilt preparation is IncomingPacs002Reply, with explicit IncomingReplyContext and IncomingReplyDecision. The builder does not generate IDs or read the clock. Preserve independent schema/signature tests and explicit Development-only unsigned policy.

## Established implementation direction

Own reply artifacts by journal receipt. Different sequences may share a canonical payment but have different original protocol references, so payment-level storage alone cannot represent every reply. Repeated delivery of the same participant/sequence reuses its journal/reply identity. No reply path initiates a second CBS submission.

Under receipt ownership, freeze reply IDs, creation time, recipient/participant and mapping context, original references, and the immutable decision. Normal replies read the canonical payment's stored IPS decision; trusted FF01 replies store a receipt-scoped decision and require no artificial payment aggregate. Missing/nonpositive sequences, untrusted inputs and conflicting payment contents remain held without automatic reply. Do not enable unsupported message types.

Persist unsigned XML, then the selected signed message/disposition through explicit preparation checkpoints. Generate identifiers once; retries reuse the same identifiers/time and exact stored message. Missing/unusable certificates do not silently become unsigned messages. A restart or configuration change must not regenerate an already prepared message.

Create durable delivery work atomically with the ready artifact. Preparation and delivery both acquire the receipt claim through the existing scoped context and general unit of work. This shared fence prevents processing and delivery from overlapping on the same receipt; independent ownership tables are unnecessary for this sequential workflow. No transaction spans signing/transport calls where unnecessary. Store each delivery marker before remote I/O and complete response/failure evidence before interpretation. Claims, expiry and rowversion fence stale results. Saved responses are interpreted before deciding on another attempt. Lost replies remain uncertain until the approved delivery policy resolves them.

Successful delivery and receipt completion commit together. Neither prepared XML nor an attempted send alone marks the journal Processed. Failed/exhausted delivery remains diagnosable and cannot be rediscovered as fresh payment intake. CBS reconciliation remains independent; successful or failed reply delivery never rewrites the stored IPS decision or proves reversal completion.

Application owns preparation/delivery workflows and narrow external ports; Infrastructure owns SQL, signing/XML adaptation and later HTTP transport. Use an independent transport simulator in this slice. Add runtime settings through the established appsettings/typed-settings boundary. Generate migrations with official EF CLI and preserve the fresh-database-only chain.

## Approved delivery retry policy

Owner confirmed on 2026-10-05: two total delivery attempts, 200 ms apart, then durable manual review. Persist the attempt budget across restarts; never reset it when rediscovering work. Expose the defaults through typed settings and appsettings. A persisted submission marker consumes an attempt even when a crash leaves no response evidence.

## Uploaded worker design and subsequent integration

Owner supplied `C:/Users/omo/Downloads/inbound-payment-flow.md` on 2026-10-05 as the polling-worker design. Retain these requirements in the subsequent live-integration specification:

- Three independently configurable worker pools: Receive, Processing, and Response/Retry. Receive supports concurrent GetMessage calls, each returning at most one message. Persist the receipt before notifying its ID, then continue receiving without waiting for business processing.
- Two bounded channels carry only journal IDs: InboundProcessingChannel and OutboundRetryChannel. SQL remains authoritative; notification is nonblocking and committed work is rediscoverable after a full channel, lost notification or restart.
- Processing acquires durable ownership and dispatches by message type to implemented Application workflows. The document's other handler names are examples for future capabilities, not placeholder implementations to enable now.
- Prepare and persist the immutable reply before the first inline delivery attempt. Response/Retry workers use the same delivery workflow and exact stored message, without repeating CBS submission, deciding the payment again or regenerating XML.
- Recovery includes ready replies that crashed before their first send, as well as uncertain attempts and scheduled retries. Queued IDs confer no ownership; initial sends and recovery must compete through the same SQL fencing.

Carry forward the approved refinements: receiving participant plus EndToEndId identifies the canonical payment, and EndToEndId remains the CBS idempotency key (the document's generic PaymentId wording does not replace it). Reply correlation is per receipt. pacs.008 receives no MessageAck. Keep approved oldest-due-first discovery; the diagram's priority scheduling is an unresolved future strategy, not an instruction to replace current ordering.

Specify receive/processing/retry concurrency counts, empty-response delay, receive timeout/error backoff, connection limits, shutdown behavior and multi-instance receive coordination before enabling live integration. Expose operational values in settings. Existing approved CBS deadline budgets remain in force. This upload establishes the worker structure; it does not supply exact values for these remaining operational choices.

## Protocol evidence checked on 2026-10-05

Repository document `docs/business-docs/2. GE_IPS_Inception_Report_Annex_D_IPS-Participant_Interface_v.1.02.pdf`:

- Pages 92 and 94: ReplyToPayment returns a pacs.002; repeating it is explicitly safe and returns the payment's final status. An unknown communication result is not proof of rejection. This supports replaying the exact stored reply, without any second CBS submission.
- Pages 98-100, sections 6.2-6.3: POST /Message serves payment replies. HTTP 200 alone does not establish message acceptance or payment completion. The documented request statuses are ACCP and RJCT/<ErrorCode>, with a pacs.002 response on HTTP 200. The source test value SUCCESS is not the documented production success value. The old receiver ignores this returned evidence; do not reproduce that shortcut.
- Page 93: after GetMessage returns a message or no message, poll again within the operator-defined interval (the document gives five seconds). Worker delays must respect the configured operator limit, rather than introducing an independent arbitrary polling interval.

`docs/business-docs/1. GE_IPS_Participant_API_Integration_v.1.00.pdf`, pages 3-4, explicitly supports multiple connections and parallel sending/receiving, with a thread-safe pooled HTTP client. This supports concurrent receive workers. Multiple service instances must share SQL deduplication and claims; each has local channels. Do not impose a singleton receiver without a demonstrated protocol or deployment requirement. Operator connection limits still need configuration and verification.

The original client calls EnsureSuccessStatusCode, then parses ReqSts and a nonempty body; the receiver treats any normally returned result as delivery. The new interpreter must distinguish transport completion, the IPS final report and consistency with the immutable local decision. Invalid signatures, missing or mismatched correlation, missing/malformed status and contradictory evidence must never silently mark a receipt Processed. Exact pacs.002 correlation fields and consistent rejection handling require fixtures before implementation.

## Provisional late-reply policy and future question

Reuse the frozen reply within its durable two-attempt budget even after the original business-processing deadline; that deadline must not turn a stored ACCP into RJCT. Interpret conclusive saved evidence before considering another attempt. An unresolved exhausted budget becomes manual review. A contradictory final IPS report also requires manual review while retaining the original local decision and all evidence. Owner approved on 2026-10-05: a repeated receipt already conclusively completed increments duplicate metadata without initiating another send or resetting its attempt budget.

The documents inspected specify safe repeated ReplyToPayment calls but no maximum retention/replay window. Do not invent a guaranteed IPS retention period. Establish actual behavior with independent fixtures and later interoperability verification; protocol refusal or unresolved evidence cannot count as delivery success. Owner provisionally agreed on 2026-10-05 to send the saved reply after the deadline and explicitly requested that the remaining question be retained for future sessions.

**Open question — IPS late-reply acceptance and retention:** How long after the original processing deadline does IPS accept an initial or repeated pacs.002, and what final response does it return if the original payment has timed out or its record is no longer retained? Confirm with the IPS operator or authoritative protocol clarification and interoperability tests before production activation. Cover both a saved-but-never-sent reply and a previously sent reply with a lost response. Do not infer an unlimited replay guarantee from safe retry wording.

Until resolved, use the provisional approach: send the exact frozen reply within the remaining durable attempt budget, including after restart; never repeat CBS submission or change the stored decision. Preserve and interpret the returned evidence. Contradictory outcomes or exhausted unresolved attempts require manual review. This open protocol question does not block implementing the provisional workflow against a simulator; it remains a live-integration verification item.

Microsoft resilience may provide retry mechanics. Two total attempts mean one retry, with a constant 200 ms delay and no jitter. SQL remains authoritative for the remaining budget across processes and restarts; each send requires committed ownership and an attempt marker. Do not stack an independent HTTP retry budget underneath worker retries. Verify the pipeline placement during implementation so no hidden send bypasses durable accounting.

## Implementation scope and evidence interpretation

The concrete Application workflow acquires and commits receipt ownership, snapshots the stored payment decision (or a trusted FF01 rejection), original references, mapping profile, generated identifiers and attempt limit, then commits unsigned and signed XML separately. Preparation defers on unavailable certificates; it never spends a delivery attempt. Each invocation performs at most one send, scheduling the next durable attempt 200 ms after unresolved completion. A restart with a marker only conservatively consumes that attempt. Recovery is via existing due-receipt discovery; live channel routing and inline first-send composition belong to the next integration slice.

Infrastructure owns immutable per-receipt IncomingReplies and IncomingReplyAttempts records. Every mutation touches the rowversion-protected journal parent. Unique receipt/attempt-number pairs, committed ownership checks, write-once evidence and the shared transaction protect concurrent execution. A final reply outcome commits with receipt Processed or Held. No payment decision or CBS follow-up is changed by this workflow.

HTTP 200 requires one documented ACCP or RJCT/numeric-code request status and an independently verifiable, schema-valid IPS pacs.002. Interpret the final status of the original pacs.008 using its original group/transaction/EndToEndId references, with the frozen IPS/participant envelope identities. Missing, malformed, unsigned, mismatched or internally contradictory evidence stays unresolved. A conclusive final outcome consistent with the stored decision completes delivery; an opposite final outcome requires manual review. This is stricter than the source's normally-returned-call shortcut and follows the supplied protocol evidence; real IPS correlation/rejection behavior remains an interoperability check before activation.

The host binds reply attempt count, retry delay, call/persistence/ownership budgets and preparation retry delay. Freeze each reply's attempt limit so configuration changes cannot replenish it. Initial defaults: two attempts, 200 ms retry delay, 20 s call timeout, 2 s persistence budget, 45 s ownership, 5 s preparation retry. Clamp transport to remaining ownership minus persistence time. No HTTP client is activated here; its port performs exactly one send. Microsoft resilience integration belongs to the later live adapter and must preserve that durable-send boundary, not add hidden retries.

The uploaded worker requirements belong to the subsequent live-integration specification. This slice establishes the durable reply workflow that those workers will invoke. The open IPS late-acceptance/retention question above remains recorded for that stage.

## Verification

Cover per-receipt correlation across identical payments, same-sequence duplicates, FF01 without a payment, held/untrusted entries, immutable IDs/XML across restart and settings changes, certificate failures, prepared-but-unnotified work, marker/result replay, competing claims, stale responses, atomic completion/rollback, cancellation, configured attempt exhaustion and no MessageAck for pacs.008. Use real SQL, independent XML/signature fixtures and a simulator recording remote receipt before a lost reply.

Run build, full tests, formatting, architecture/Contracts, generated model/migration checks and independent Standards/Spec reviews. No production polling, live clients, endpoints, acknowledgement of unsupported types or deployment is included. After approval and completion, proceed to the live receive, processing and response/retry workers.
