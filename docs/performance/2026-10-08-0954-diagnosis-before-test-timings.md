# Outgoing payment performance, 2026-10-08 09:54 UTC

Measured with `tests/IPS.Middleware.Performance` as specified in [013](../specs/013-measured-performance.md), from 2026-10-08 09:54:37 to 09:58:43 UTC.

**Verdict: fail.** Not met: Settlement latency p95; Settlement latency p99; Accepted payments not sent to IPS; 5xx responses; Every response is 200 or 504. The evidence is below.

## Targets

| Target | Limit | Observed | Result |
|---|---|---|---|
| Settlement latency p95 | <= 1000 ms | 17115.7 ms (9000 samples) | **fail** |
| Settlement latency p99 | <= 2000 ms | 23870.7 ms (9000 samples) | **fail** |
| Lost payments (accepted, no final callback) | 0 | 0 (without a callback 0, callbacks but none final 0) | pass |
| Accepted payments not sent to IPS | 0 | 282 (never received by the simulated IPS 282, reported NotSent 282) | **fail** |
| Duplicate sends | 0 | 0 (flagged possible-duplicate resends of the same bytes, allowed: 0) | pass |
| Duplicate callbacks without an unknown delivery outcome before them | 0 | 0 (payments with more than one callback: 5) | pass |
| 5xx responses | 0 | 243 (504: 232; requests without a response: 0) | **fail** |
| Every response is 200 or 504 | 0 others | 11 others (200: 9357, 504: 232, 500: 11) | **fail** |
| Readiness Healthy throughout | every sample of every instance | 98 of 98 samples Healthy, 2 of 2 instances sampled | pass |
| Generator lag p99 | <= 50 ms | 15.0 ms (9000 samples) | pass |
| Work due after the drain (ips.backlog.due) | 0 | 0 (outgoing_payments 0, incoming_payments 0, incoming_transfers 0, inbound_receipts 0, callbacks 0) | pass |

## Latency (measured window, milliseconds)

Settlement runs from the generator's send to the simulated core's receipt of the payment's final callback; response from the send to the API's HTTP response; lag from a request's planned start to its actual start. Both ends are read from this machine's clock. Percentiles are exact (nearest rank over every sample); the verdict compares the unrounded values.

| | Samples | Min | Mean | p50 | p95 | p99 | Max |
|---|---|---|---|---|---|---|---|
| Settlement | 9000 | 217.1 | 2756.2 | 606.2 | 17115.7 | 23870.7 | 27191.5 |
| Response | 9000 | 216.9 | 2110.2 | 356.1 | 15782.5 | 21041.1 | 21132.8 |
| Generator lag | 9000 | -2.1 | 7.0 | 6.9 | 13.8 | 15.0 | 113.4 |

## Throughput

- Planned: 50 requests per second; sent: 50.00 per second; callbacks received by the simulated core: 49.35 per second (over the measured window).
- Most requests open at once: 221, against 16 execution slots (2 instances at concurrency 8).
- 6403 requests started while at least 16 requests were open: their payments may have waited for admission, so the concurrency limit, not the code, can bound their latency (013 risk).

## Correctness (everything sent, warm-up included)

| Check | Count |
|---|---|
| Payments sent | 9600 |
| Responses by status code | 200: 9357, 504: 232, 500: 11 |
| Accepted by the service (200 or 504) | 9589 |
| 5xx responses (504 included) | 243 |
| 504 responses | 232 |
| Responses other than 200 or 504 (no response included) | 11 |
| Requests without a response | 0 |
| Accepted, not sent: never received by the simulated IPS | 282 |
| Accepted, not sent: reported NotSent to the core | 282 |
| Accepted, not sent (either of the two above) | 282 |
| Lost: accepted, without a callback | 0 |
| Lost: accepted, callbacks but none final | 0 |
| Lost (either of the two above) | 0 |
| Sent more than once other than as a flagged resend of the same bytes | 0 |
| Flagged possible-duplicate resends of the same bytes (allowed) | 0 |
| More than one callback | 5 |
| More than one callback and more callbacks than recorded delivery attempts (no unknown outcome before the repeat) | 0 |
| Investigations (pacs.028) received by the simulated IPS / payments investigated | 0 / 0 |
| Other IPS messages by definition | none |
| IPS messages / callbacks for no payment of the run or unreadable | 0 / 0 |

API answers by status code and body (the 10 most frequent):

| Answer | Count |
|---|---|
| 200 Accepted | 9307 |
| 504 Processing | 232 |
| 200 NotSent TM01/1015 | 50 |
| 500 System.InvalidOperationException: An exception has been raised that is likely due to a transient failure. Consider enabling transient error resiliency by adding | 11 |

Final status the core was told, with reason code and IPS internal code:

| Final callback | Payments |
|---|---|
| Accepted | 9307 |
| NotSent TM01/1015 | 293 |

Examples, the first payments with each problem:

- not answered 200, accepted, not sent: perf-3ad3933ef546: sent 09:57:45.299 to middleware-2, answered 504 Processing, IPS sends 0 (flagged 0), investigations 0, callbacks [NotSent TM01/1015], deliveries [#3 Delivered after 1 attempts]
- not answered 200, accepted, not sent: perf-454f883b85bd: sent 09:57:45.329 to middleware-1, answered 504 Processing, IPS sends 0 (flagged 0), investigations 0, callbacks [NotSent TM01/1015], deliveries [#3 Delivered after 1 attempts]
- not answered 200, accepted, not sent: perf-223783182e00: sent 09:57:45.344 to middleware-2, answered 504 Processing, IPS sends 0 (flagged 0), investigations 0, callbacks [NotSent TM01/1015], deliveries [#3 Delivered after 1 attempts]
- not answered 200, accepted, not sent: perf-1f7cc0c97ee5: sent 09:57:45.360 to middleware-1, answered 504 Processing, IPS sends 0 (flagged 0), investigations 0, callbacks [NotSent TM01/1015], deliveries [#3 Delivered after 1 attempts]
- not answered 200, accepted, not sent: perf-efee38b44179: sent 09:57:45.484 to middleware-1, answered 504 Processing, IPS sends 0 (flagged 0), investigations 0, callbacks [NotSent TM01/1015], deliveries [#3 Delivered after 1 attempts]
- accepted, not sent: perf-9c31e096de97: sent 09:57:38.723 to middleware-1, answered 200 NotSent TM01/1015, IPS sends 0 (flagged 0), investigations 0, callbacks [NotSent TM01/1015], deliveries [#5 Delivered after 1 attempts]
- accepted, not sent: perf-d8bd82f928c3: sent 09:57:38.754 to middleware-2, answered 200 NotSent TM01/1015, IPS sends 0 (flagged 0), investigations 0, callbacks [NotSent TM01/1015], deliveries [#5 Delivered after 1 attempts]
- accepted, not sent: perf-b3773991fd79: sent 09:57:38.769 to middleware-1, answered 200 NotSent TM01/1015, IPS sends 0 (flagged 0), investigations 0, callbacks [NotSent TM01/1015], deliveries [#5 Delivered after 1 attempts]
- accepted, not sent: perf-2de3a527f204: sent 09:57:38.783 to middleware-2, answered 200 NotSent TM01/1015, IPS sends 0 (flagged 0), investigations 0, callbacks [NotSent TM01/1015], deliveries [#3 Delivered after 1 attempts]
- accepted, not sent: perf-9994be9875ea: sent 09:57:38.814 to middleware-1, answered 200 NotSent TM01/1015, IPS sends 0 (flagged 0), investigations 0, callbacks [NotSent TM01/1015], deliveries [#3 Delivered after 1 attempts]
- reported twice: perf-94a62fed9d44: sent 09:55:49.288 to middleware-1, answered 200 Accepted, IPS sends 1 (flagged 0), investigations 0, callbacks [Accepted, Accepted], deliveries [#6 Delivered after 2 attempts, last failure: Previous delivery owner expired; CBS receipt is unknown.]
- reported twice: perf-92023a67c163: sent 09:56:19.433 to middleware-2, answered 200 Accepted, IPS sends 1 (flagged 0), investigations 0, callbacks [Accepted, Accepted], deliveries [#6 Delivered after 2 attempts, last failure: Previous delivery owner expired; CBS receipt is unknown.]
- reported twice: perf-59e67023c736: sent 09:56:48.944 to middleware-2, answered 200 Accepted, IPS sends 1 (flagged 0), investigations 0, callbacks [Accepted, Accepted], deliveries [#6 Delivered after 2 attempts, last failure: Previous delivery owner expired; CBS receipt is unknown.]
- reported twice: perf-61e371593494: sent 09:57:12.103 to middleware-2, answered 200 Accepted, IPS sends 1 (flagged 0), investigations 0, callbacks [Accepted, Accepted], deliveries [#6 Delivered after 2 attempts, last failure: Previous delivery owner expired; CBS receipt is unknown.]
- reported twice: perf-25406cc5f9d0: sent 09:57:33.864 to middleware-2, answered 200 Accepted, IPS sends 1 (flagged 0), investigations 0, callbacks [Accepted, Accepted], deliveries [#6 Delivered after 2 attempts, last failure: Previous delivery owner expired; CBS receipt is unknown.]

## By instance (everything sent)

| Instance | Sent | Responses | Accepted, not sent | Lost | More than one callback | First failure (send time) |
|---|---|---|---|---|---|---|
| middleware-1 | 4800 | 200: 4676, 504: 117, 500: 7 | 142 | 0 | 1 | 09:57:38.723 |
| middleware-2 | 4800 | 200: 4681, 504: 115, 500: 4 | 140 | 0 | 4 | 09:57:38.754 |

## Timeline (by send time, one row per minute)

| From (UTC) | Phase | Sent | 200 | 504 | Other 5xx | Other | Not sent | More than one callback | Settlement p95 (ms) |
|---|---|---|---|---|---|---|---|---|---|
| 09:54:37 | WarmUp | 601 | 601 | 0 | 0 | 0 | 0 | 0 | 419 |
| 09:55:37 | Measured | 2999 | 2999 | 0 | 0 | 0 | 0 | 2 | 2446 |
| 09:56:37 | Measured | 3000 | 3000 | 0 | 0 | 0 | 0 | 3 | 16253 |
| 09:57:37 | Measured | 3000 | 2757 | 232 | 11 | 0 | 282 | 0 | 22996 |

## API logs during the run

Warnings and errors each instance wrote from the start of the load to the end of the drain, from the orchestrator's log stream, by level (lower levels are skipped); then the most frequent by template (category and first message line, numbers and identifiers normalised, at most 80 characters) with the first such entry.

| Instance | Entries by level |
|---|---|
| middleware-1 | fail: 13 |
| middleware-2 | fail: 11 |

| Instance | Level | Count | Template | First entry |
|---|---|---|---|---|
| middleware-1 | fail | 7 | Microsoft.AspNetCore.Diagnostics.DeveloperExceptionPageMiddleware: An unhandled  | Microsoft.AspNetCore.Diagnostics.DeveloperExceptionPageMiddleware: An unhandled exception has occurred while executing the request. \| System.InvalidOperationException: An exception has been raised that is likely due to a transient failure. Consider enabling transient error resiliency by adding 'Enab |
| middleware-1 | fail | 2 | IPS.Middleware.Infrastructure.Payments.Execution.OutgoingRuntime: Outgoing callb | IPS.Middleware.Infrastructure.Payments.Execution.OutgoingRuntime: Outgoing callback discovery failed; the next sweep will retry \| Microsoft.Data.SqlClient.SqlException (0x80131904): Transaction (Process ID 190) was deadlocked on lock resources with another process and has been chosen as the deadlock |
| middleware-1 | fail | 2 | Microsoft.EntityFrameworkCore.Database.Transaction: An error occurred using a tr | Microsoft.EntityFrameworkCore.Database.Transaction: An error occurred using a transaction. |
| middleware-1 | fail | 2 | Microsoft.EntityFrameworkCore.Query: An exception occurred while iterating over  | Microsoft.EntityFrameworkCore.Query: An exception occurred while iterating over the results of a query for context type 'IPS.Middleware.Infrastructure.Transactions.TransactionDbContext'. \| Microsoft.Data.SqlClient.SqlException (0x80131904): Transaction (Process ID 190) was deadlocked on lock resourc |
| middleware-2 | fail | 4 | Microsoft.AspNetCore.Diagnostics.DeveloperExceptionPageMiddleware: An unhandled  | Microsoft.AspNetCore.Diagnostics.DeveloperExceptionPageMiddleware: An unhandled exception has occurred while executing the request. \| System.InvalidOperationException: An exception has been raised that is likely due to a transient failure. Consider enabling transient error resiliency by adding 'Enab |
| middleware-2 | fail | 3 | Microsoft.EntityFrameworkCore.Database.Transaction: An error occurred using a tr | Microsoft.EntityFrameworkCore.Database.Transaction: An error occurred using a transaction. |
| middleware-2 | fail | 2 | IPS.Middleware.Infrastructure.Payments.Execution.OutgoingRuntime: Outgoing callb | IPS.Middleware.Infrastructure.Payments.Execution.OutgoingRuntime: Outgoing callback discovery failed; the next sweep will retry \| Microsoft.Data.SqlClient.SqlException (0x80131904): Transaction (Process ID 213) was deadlocked on lock resources with another process and has been chosen as the deadlock |
| middleware-2 | fail | 2 | Microsoft.EntityFrameworkCore.Query: An exception occurred while iterating over  | Microsoft.EntityFrameworkCore.Query: An exception occurred while iterating over the results of a query for context type 'IPS.Middleware.Infrastructure.Transactions.TransactionDbContext'. \| Microsoft.Data.SqlClient.SqlException (0x80131904): Transaction (Process ID 213) was deadlocked on lock resourc |

## Database diagnosis

Query Store captured every statement from the start of the load to the end of the drain. The costliest statements by total duration, with the plan each ran with most often (operators in tree order, with the table and index read); durations in milliseconds; failed executions were aborted (a command timeout or cancellation) or ended in an error; waits are Query Store's categories.

| Query | Executions (failed) | Total | Mean | Max | CPU | Mean logical reads | Waits | Statement | Plan |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 6099 (0) | 82479 | 13.5 | 1437 | 1689 | 52 | Lock 80815, Memory 13, CPU 1 | `(@status int,@now datetimeoffset(7),@p int)SELECT TOP(@p) [t].[Id] FROM [Transactions] AS [t] LEFT JOIN [Transactions] AS [t0] ON [t].[Id] = [t0].[Id] WHERE [t0].[CurrentStatus] = @status AND [t].[ClaimToken] IS NULL AND ([t].[NextActionAtUtc] IS NULL OR [t].[NextActionAtUtc] <= @now) ORDER BY CASE WHEN [t0].[MessageType] = N'pacs.008' THEN 0 ELSE 1 END, [t0].[CurrentStatusAtUtc], [t].[Id]` | Top > Nested Loops > Sort > Compute Scalar > Index Seek [Transactions].[IX_Transactions_CurrentStatus_MessageType_CurrentStatusAtUtc_Id] > Clustered Index Seek [Transactions].[PK_Transactions] |
| 12 | 177373 (0) | 77452 | 0.4 | 370 | 12366 | 8 | Lock 64943, Buffer Latch 82, CPU 34 | `(@reference nvarchar(35))SELECT TOP(2) [t].[Id], [t].[AcceptedJson], [t].[ClaimExpiresAtUtc], [t].[ClaimToken], [t].[Direction], [t].[MessageId], [t].[NextActionAtUtc], [t].[ProtocolTransactionId], [t].[RequestJson], [t].[RowVersion], [t].[UnsignedXml], [t0].[Id], [t0].[AggregateKind], [t0].[ClientReference], [t0].[CreatedAtUtc], [t0].[CurrentDescription], [t0].[CurrentIpsInternalCode], [t0].[CurrentReasonCode], [t0].[CurrentSequence], [t0].[CurrentSource], [t0].[CurrentStatus], [t0].[CurrentStatusAtUtc], [t0].[LastSequence], [t0].[MessageType], [t0].[RecallRefusedAtUtc], [t0].[RowVersion] FRO...` | Compute Scalar > Top > Nested Loops > Nested Loops > Index Seek [Transactions].[IX_Transactions_ClientReference] > Clustered Index Seek (lookup) [Transactions].[PK_Transactions] > Clustered Index Seek [Transactions].[PK_Transactions] |
| 7 | 2129 (4) | 37638 | 17.7 | 3354 | 857 | 111 | Lock 36634, Buffer Latch 132, CPU 3 | `(@now datetimeoffset(7),@p int)SELECT TOP(@p) [o].[PaymentId], [o].[Sequence] FROM [OutgoingStatusDeliveries] AS [o] WHERE EXISTS ( SELECT 1 FROM [Transactions] AS [t] WHERE [t].[Id] = [o].[PaymentId] AND [t].[CurrentSequence] = [o].[Sequence]) AND [o].[State] = 0 AND CASE WHEN [o].[ClaimToken] IS NULL THEN CASE WHEN [o].[NextAtUtc] <= @now THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END WHEN [o].[ClaimExpiresAtUtc] <= @now THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END = CAST(1 AS bit) ORDER BY [o].[NextAtUtc], [o].[PaymentId], [o].[Sequence]` | Top > Nested Loops > Nested Loops > Index Seek [OutgoingStatusDeliveries].[IX_OutgoingStatusDeliveries_State_NextAtUtc_PaymentId_Sequence] > Clustered Index Seek (lookup) [OutgoingStatusDeliveries].[PK_OutgoingStatusDeliveries] > Clustered Index Seek [Transactions].[PK_Transactions] |
| 33 | 9605 (5) | 13705 | 1.4 | 2004 | 800 | 10 | Lock 12805, Buffer Latch 77, Buffer IO 4 | `(@p5 uniqueidentifier,@p6 int,@p7 varbinary(8),@p0 datetimeoffset(7),@p1 uniqueidentifier,@p2 datetimeoffset(7),@p3 datetimeoffset(7),@p4 int)UPDATE [OutgoingStatusDeliveries] SET [ClaimExpiresAtUtc] = @p0, [ClaimToken] = @p1, [DeliveredAtUtc] = @p2, [NextAtUtc] = @p3, [State] = @p4 OUTPUT INSERTED.[RowVersion] WHERE [PaymentId] = @p5 AND [Sequence] = @p6 AND [RowVersion] = @p7` | Assert > Clustered Index Update > Compute Scalar > Compute Scalar > Top > Compute Scalar > Clustered Index Seek [OutgoingStatusDeliveries].[PK_OutgoingStatusDeliveries] |
| 31 | 10647 (0) | 12692 | 1.2 | 218 | 793 | 4 | Lock 11021, Latch 870, CPU 2 | `(@p3 uniqueidentifier,@p4 int,@p5 varbinary(8),@p0 int,@p1 datetimeoffset(7),@p2 uniqueidentifier)UPDATE [OutgoingStatusDeliveries] SET [Attempts] = @p0, [ClaimExpiresAtUtc] = @p1, [ClaimToken] = @p2 OUTPUT INSERTED.[RowVersion] WHERE [PaymentId] = @p3 AND [Sequence] = @p4 AND [RowVersion] = @p5` | Assert > Clustered Index Update > Compute Scalar > Clustered Index Seek [OutgoingStatusDeliveries].[PK_OutgoingStatusDeliveries] |
| 30 | 31108 (0) | 6237 | 0.2 | 66 | 3658 | 8 | Lock 2563, CPU 1 | `(@key_PaymentId uniqueidentifier)SELECT TOP(2) [t].[Id], [t].[AcceptedJson], [t].[ClaimExpiresAtUtc], [t].[ClaimToken], [t].[Direction], [t].[MessageId], [t].[NextActionAtUtc], [t].[ProtocolTransactionId], [t].[RequestJson], [t].[RowVersion], [t].[UnsignedXml], [t0].[Id], [t0].[AggregateKind], [t0].[ClientReference], [t0].[CreatedAtUtc], [t0].[CurrentDescription], [t0].[CurrentIpsInternalCode], [t0].[CurrentReasonCode], [t0].[CurrentSequence], [t0].[CurrentSource], [t0].[CurrentStatus], [t0].[CurrentStatusAtUtc], [t0].[LastSequence], [t0].[MessageType], [t0].[RecallRefusedAtUtc], [t0].[RowVers...` | Top > Nested Loops > Clustered Index Seek [Transactions].[PK_Transactions] > Compute Scalar > Clustered Index Seek [Transactions].[PK_Transactions] |
| 14 | 9941 (0) | 6158 | 0.6 | 120 | 952 | 18 | Lock 5181, Buffer Latch 9, Tran Log IO 4 | `(@p6 uniqueidentifier,@p7 varbinary(8),@p0 datetimeoffset(7),@p1 uniqueidentifier,@p2 int,@p3 int,@p4 datetimeoffset(7),@p5 int)UPDATE [Transactions] SET [ClaimExpiresAtUtc] = @p0, [ClaimToken] = @p1, [CurrentSequence] = @p2, [CurrentStatus] = @p3, [CurrentStatusAtUtc] = @p4, [LastSequence] = @p5 OUTPUT INSERTED.[RowVersion], INSERTED.[AggregateKind] WHERE [Id] = @p6 AND [RowVersion] = @p7` | Compute Scalar > Assert > Clustered Index Update > Compute Scalar > Compute Scalar > Top > Compute Scalar > Clustered Index Seek [Transactions].[PK_Transactions] |
| 22 | 18620 (0) | 4649 | 0.2 | 218 | 4399 | 48 | Memory 436, Preemptive 268, Latch 133 | `(@p0 uniqueidentifier,@p1 nvarchar(4000),@p2 datetimeoffset(7),@p3 int,@p4 int,@p5 nvarchar(2000),@p6 nvarchar(4000),@p7 int,@p8 uniqueidentifier,@p9 nvarchar(35),@p10 uniqueidentifier,@p11 uniqueidentifier,@p12 datetimeoffset(7),@p13 uniqueidentifier,@p14 datetimeoffset(7),@p15 int,@p16 uniqueidentifier)INSERT INTO [OutgoingMessages] ([Id], [Content], [CreatedAtUtc], [Direction], [Disposition], [Failure], [HeadersJson], [HttpStatusCode], [InvestigationId], [MessageDefinition], [OriginatingMessageId], [PaymentId], [ProcessedAtUtc], [ResendId], [StartedAtUtc], [Status], [SubmissionOwner]) VALUE...` | Assert > Nested Loops > Assert > Nested Loops > Nested Loops > Nested Loops > Assert > Clustered Index Insert > Compute Scalar > Compute Scalar > Constant Scan > Index Seek [OutgoingInvestigations].[AK_OutgoingInvestigations_Id_PaymentId] > Index Seek [OutgoingResends].[AK_OutgoingResends_Id_PaymentId] > Clustered Index Seek [Transactions].[PK_Transactions] > Index Seek [OutgoingMessages].[AK_OutgoingMessages_Id_PaymentId] |
| 29 | 31108 (0) | 3662 | 0.1 | 68 | 1306 | 6 | Lock 2342, Buffer Latch 1 | `(@key_PaymentId uniqueidentifier,@key_Sequence int)SELECT TOP(2) [o].[PaymentId], [o].[Sequence], [o].[Attempts], [o].[ClaimExpiresAtUtc], [o].[ClaimToken], [o].[DeliveredAtUtc], [o].[LastFailure], [o].[NextAtUtc], [o].[PayloadJson], [o].[PayloadVersion], [o].[RowVersion], [o].[State] FROM [OutgoingStatusDeliveries] AS [o] WHERE EXISTS ( SELECT 1 FROM [Transactions] AS [t] WHERE [t].[Id] = [o].[PaymentId] AND [t].[CurrentSequence] = [o].[Sequence]) AND [o].[PaymentId] = @key_PaymentId AND [o].[Sequence] = @key_Sequence` | Top > Nested Loops > Clustered Index Seek [OutgoingStatusDeliveries].[PK_OutgoingStatusDeliveries] > Clustered Index Seek [Transactions].[PK_Transactions] |
| 13 | 30982 (0) | 3526 | 0.1 | 41 | 1175 | 6 | Lock 2334, Buffer Latch 3, CPU 3 | `(@id uniqueidentifier)SELECT TOP(2) [t].[Id], [t].[AcceptedJson], [t].[ClaimExpiresAtUtc], [t].[ClaimToken], [t].[Direction], [t].[MessageId], [t].[NextActionAtUtc], [t].[ProtocolTransactionId], [t].[RequestJson], [t].[RowVersion], [t].[UnsignedXml], [t0].[Id], [t0].[AggregateKind], [t0].[ClientReference], [t0].[CreatedAtUtc], [t0].[CurrentDescription], [t0].[CurrentIpsInternalCode], [t0].[CurrentReasonCode], [t0].[CurrentSequence], [t0].[CurrentSource], [t0].[CurrentStatus], [t0].[CurrentStatusAtUtc], [t0].[LastSequence], [t0].[MessageType], [t0].[RecallRefusedAtUtc], [t0].[RowVersion] FROM [...` | Top > Nested Loops > Clustered Index Seek [Transactions].[PK_Transactions] > Compute Scalar > Clustered Index Seek [Transactions].[PK_Transactions] |
| 43 | 6 (0) | 3451 | 575.2 | 3451 | 1 | 10 | Lock 3233, Latch 217 | `(@p4 uniqueidentifier,@p5 int,@p6 varbinary(8),@p0 datetimeoffset(7),@p1 uniqueidentifier,@p2 nvarchar(2000),@p3 datetimeoffset(7))UPDATE [OutgoingStatusDeliveries] SET [ClaimExpiresAtUtc] = @p0, [ClaimToken] = @p1, [LastFailure] = @p2, [NextAtUtc] = @p3 OUTPUT INSERTED.[RowVersion] WHERE [PaymentId] = @p4 AND [Sequence] = @p5 AND [RowVersion] = @p6` | Assert > Clustered Index Update > Compute Scalar > Compute Scalar > Top > Compute Scalar > Clustered Index Seek [OutgoingStatusDeliveries].[PK_OutgoingStatusDeliveries] |
| 25 | 18614 (0) | 3169 | 0.2 | 7 | 3159 | 16 | Memory 192, Buffer Latch 3, CPU 1 | `(@p15 uniqueidentifier,@p0 nvarchar(4000),@p1 datetimeoffset(7),@p2 int,@p3 int,@p4 nvarchar(2000),@p5 nvarchar(4000),@p6 int,@p7 uniqueidentifier,@p8 nvarchar(35),@p9 uniqueidentifier,@p10 datetimeoffset(7),@p11 uniqueidentifier,@p12 datetimeoffset(7),@p13 int,@p14 uniqueidentifier)UPDATE [OutgoingMessages] SET [Content] = @p0, [CreatedAtUtc] = @p1, [Direction] = @p2, [Disposition] = @p3, [Failure] = @p4, [HeadersJson] = @p5, [HttpStatusCode] = @p6, [InvestigationId] = @p7, [MessageDefinition] = @p8, [OriginatingMessageId] = @p9, [ProcessedAtUtc] = @p10, [ResendId] = @p11, [StartedAtUtc] = @p...` | Compute Scalar > Sequence > Table Spool > Split > Assert > Nested Loops > Nested Loops > Assert > Compute Scalar > Clustered Index Update > Compute Scalar > Compute Scalar > Compute Scalar > Clustered Index Seek [OutgoingMessages].[PK_OutgoingMessages] > Index Seek [OutgoingInvestigations].[AK_OutgoingInvestigations_Id_PaymentId] > Index Seek [OutgoingResends].[AK_OutgoingResends_Id_PaymentId] > Assert > Nested Loops > Table Spool > Index Seek [OutgoingMessages].[AK_OutgoingMessages_Id_PaymentId] > Filter > Table Spool |
| 11 | 28513 (0) | 2827 | 0.1 | 24 | 2704 | 19 | Memory 108, Buffer IO 90, Preemptive 19 | `(@p0 int,@p1 uniqueidentifier,@p2 varchar(32),@p3 uniqueidentifier,@p4 nvarchar(100),@p5 datetimeoffset(7),@p6 nvarchar(4000),@p7 int)INSERT INTO [TransactionEvents] ([Sequence], [TransactionId], [AggregateKind], [EventId], [Name], [OccurredAtUtc], [PayloadJson], [SchemaVersion]) VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7)` | Assert > Nested Loops > Assert > Clustered Index Insert > Index Seek [AggregateIdentities].[AK_AggregateIdentities_Id_Kind] |
| 10 | 9600 (0) | 2692 | 0.3 | 25 | 2617 | 50 | Memory 346, Buffer IO 44, Preemptive 35 | `(@p2 uniqueidentifier,@p3 nvarchar(4000),@p4 datetimeoffset(7),@p5 uniqueidentifier,@p6 int,@p7 nvarchar(35),@p8 datetimeoffset(7),@p9 nvarchar(35),@p10 nvarchar(4000),@p11 nvarchar(4000),@p12 nvarchar(35),@p13 datetimeoffset(7),@p14 nvarchar(2000),@p15 int,@p16 nvarchar(35),@p17 int,@p18 int,@p19 int,@p20 datetimeoffset(7),@p21 int,@p22 nvarchar(16),@p23 datetimeoffset(7))INSERT INTO [Transactions] ([Id], [AcceptedJson], [ClaimExpiresAtUtc], [ClaimToken], [Direction], [MessageId], [NextActionAtUtc], [ProtocolTransactionId], [RequestJson], [UnsignedXml], [ClientReference], [CreatedAtUtc], [Cur...` | Compute Scalar > Assert > Nested Loops > Assert > Clustered Index Insert > Compute Scalar > Compute Scalar > Constant Scan > Index Seek [AggregateIdentities].[AK_AggregateIdentities_Id_Kind] |
| 36 | 32 (0) | 1354 | 42.3 | 159 | 321 | 4230 | Lock 1001, CPU 25, Buffer Latch 1 | `(@now datetimeoffset(7))SELECT COUNT_BIG(*) FROM [Transactions] AS [t] WHERE [t].[NextActionAtUtc] IS NOT NULL AND [t].[NextActionAtUtc] <= @now` | Stream Aggregate > Clustered Index Scan [Transactions].[PK_Transactions] |

Server waits over the run (the container serves only this database), the largest first:

| Wait type | Waits | Milliseconds |
|---|---|---|
| SOS_WORK_DISPATCHER | 854061 | 15984306 |
| DISPATCHER_QUEUE_SEMAPHORE | 25 | 4379121 |
| PREEMPTIVE_OS_FLUSHFILEBUFFERS | 86799 | 663906 |
| SLEEP_TASK | 868 | 618526 |
| LOGMGR_QUEUE | 62462 | 486920 |
| WRITELOG | 91589 | 315537 |
| SQLTRACE_INCREMENTAL_FLUSH_SLEEP | 62 | 248015 |
| REQUEST_FOR_DEADLOCK_SEARCH | 51 | 247515 |
| HADR_FILESTREAM_IOMGR_IOCOMPLETION | 490 | 245392 |
| DIRTY_PAGE_POLL | 2443 | 245298 |
| LAZYWRITER_SLEEP | 259 | 244976 |
| XE_TIMER_EVENT | 63 | 244961 |

Deadlocks recorded by the system_health event files since the load started: 2. The JSON report holds every graph.

- 09:57:48.561:
  - keylock on Middleware.dbo.OutgoingStatusDeliveries PK_OutgoingStatusDeliveries: owners processf95792ca8 S wait; waiters processf84259848 S wait
  - keylock on Middleware.dbo.OutgoingStatusDeliveries PK_OutgoingStatusDeliveries: owners processf8006d468 X; waiters processf95792ca8 S wait
  - keylock on Middleware.dbo.OutgoingStatusDeliveries IX_OutgoingStatusDeliveries_State_NextAtUtc_PaymentId_Sequence: owners processf95792ca8 S; waiters processf8006d468 X wait
  - processf84259848 (victim) waits S on KEY: 5:72057594047954944 (f3d68b28a1cf): `unknown`
  - processf95792ca8 (victim) waits S on KEY: 5:72057594047954944 (f3d68b28a1cf): `unknown`
  - processf8006d468 waits X on KEY: 5:72057594048020480 (b4d9743d27e8): `unknown`
- 09:57:56.079:
  - keylock on Middleware.dbo.OutgoingStatusDeliveries PK_OutgoingStatusDeliveries: owners processf934308c8 X; waiters processf956008c8 S wait
  - keylock on Middleware.dbo.OutgoingStatusDeliveries IX_OutgoingStatusDeliveries_State_NextAtUtc_PaymentId_Sequence: owners processf956008c8 S; waiters processf934308c8 X wait
  - processf956008c8 (victim) waits S on KEY: 5:72057594047954944 (fd161d742ba6): `unknown`
  - processf934308c8 waits X on KEY: 5:72057594048020480 (698f0fee42e8): `unknown`

## Drain and backlog

After the last response, every payment was final and every callback delivered within 0.2 s (limit 120 s).

Work due afterwards, by the query behind the `ips.backlog.due` gauge, read from the database (the service publishes the gauge only in its own process):

| Kind | Due |
|---|---|
| outgoing_payments | 0 |
| incoming_payments | 0 |
| incoming_transfers | 0 |
| inbound_receipts | 0 |
| callbacks | 0 |

## Readiness (`/health/ready` every 5 s from warm-up to the end of the drain)

| Instance | Samples | Healthy | First | Last |
|---|---|---|---|---|
| middleware-1 | 49 | 49 | 09:54:37 | 09:58:37 |
| middleware-2 | 49 | 49 | 09:54:37 | 09:58:37 |

## Machine

- CPU: AMD Ryzen 7 7800X3D 8-Core Processor, 16 logical processors; memory 63.2 GiB.
- OS: Microsoft Windows 10.0.26200; .NET 10.0.12.
- Docker 29.6.2: 16 CPUs and 30.9 GiB for the containers.
- Containers running at the start of the load: k8s-course-control-plane (kindest/node:v1.31.0), sql-esxbbvph (mcr.microsoft.com/mssql/server:2022-latest).
- One machine ran SQL Server, the simulators, the API instances and the generator, so they competed for it (013 risk).

## Configuration

- 2 API instances run as projects, sharing one SQL Server database; requests round-robin over them.
- Warm-up: 60 s at 10 per second (not measured); measured: 180 s at 50 per second; drain limit 120 s.
- Simulated IPS: answers ACCP after 100 ms without verifying the service's signature; the simulated core takes every callback at once.
- Execution concurrency 8 per instance (shipped default 8; the Aspire tests use 4); the AppHost's test timings, as in 013.
- Outgoing messages signed with a generated ECDSA P-256 key (`Payments:Signing:AllowUnsignedInDevelopment` off).

Settings the instances ran with that differ from `src/IPS.Middleware.Api/appsettings.json` (all injected by the AppHost as environment):

| Setting | appsettings.json | Used |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | (not set) | `Development` |
| `ConnectionStrings:Middleware` | (not set) | `(the AppHost's SQL Server container)` |
| `LOGGING:CONSOLE:FORMATTERNAME` | (not set) | `simple` |
| `Payments:Outgoing:Execution:AttemptBudget` | `00:00:35` | `00:00:22` |
| `Payments:Outgoing:Execution:DiscoveryInterval` | `00:00:01` | `00:00:00.200` |
| `Payments:Outgoing:Execution:Enabled` | `False` | `true` |
| `Payments:Outgoing:Execution:HttpWait` | `00:00:30` | `00:00:21` |
| `Payments:Outgoing:Investigation:DiscoveryInterval` | `00:00:05` | `00:00:00.200` |
| `Payments:Outgoing:Pacs008:Ownership` | `00:00:45` | `00:00:30` |
| `Payments:Outgoing:Policy:Currencies:0:Code` | (not set) | `GEL` |
| `Payments:Outgoing:Protocol:IpsBic` | (empty) | `NBGEGE22` |
| `Payments:Outgoing:StatusDelivery:DiscoveryInterval` | `00:00:05` | `00:00:00.200` |
| `Payments:Outgoing:Transport:Cbs:BaseUrl` | (empty) | `https://localhost:57153` |
| `Payments:Outgoing:Transport:Cbs:CheckCertificateRevocation` | `True` | `false` |
| `Payments:Outgoing:Transport:Cbs:ServerTrust:0:Path` | (not set) | `%TEMP%/ips-aspire-f6ad4ab5f7ae4e23adb3f900a3519710/trust/simulators-server.pem` |
| `Payments:Outgoing:Transport:Enabled` | `False` | `true` |
| `Payments:Outgoing:Transport:Ips:BaseUrl` | (empty) | `https://localhost:57153` |
| `Payments:Outgoing:Transport:Ips:CheckCertificateRevocation` | `True` | `false` |
| `Payments:Outgoing:Transport:Ips:RequestTimeout` | `00:00:25` | `00:00:20` |
| `Payments:Outgoing:Transport:Ips:ServerTrust:0:Path` | (not set) | `%TEMP%/ips-aspire-f6ad4ab5f7ae4e23adb3f900a3519710/trust/simulators-server.pem` |
| `Payments:Outgoing:Transport:IpsSignatureTrust:0:Path` | (not set) | `%TEMP%/ips-aspire-f6ad4ab5f7ae4e23adb3f900a3519710/trust/ips-signing.pem` |
| `Payments:Outgoing:Transport:IpsVersion` | `1` | middleware-1 `1`, middleware-2 `2` |
| `Payments:Outgoing:Transport:ParticipantBic` | (empty) | `BAGAGE22` |
| `Payments:Outgoing:Transport:SigningCertificate:Password` | (not set) | `(generated)` |
| `Payments:Outgoing:Transport:SigningCertificate:Path` | (not set) | `%TEMP%/ips-aspire-f6ad4ab5f7ae4e23adb3f900a3519710/signing/outgoing-signing.pfx` |
| `Proxy:Enabled` | `False` | `true` |
| `Proxy:Endpoint:BaseUrl` | (empty) | `https://localhost:57153` |
| `Proxy:Endpoint:CheckCertificateRevocation` | `True` | `false` |
| `Proxy:Endpoint:ServerTrust:0:Path` | (not set) | `%TEMP%/ips-aspire-f6ad4ab5f7ae4e23adb3f900a3519710/trust/simulators-server.pem` |
| `Proxy:ParticipantBic` | (empty) | `BAGAGE22` |
| `Proxy:ProxyBic` | (empty) | `PROXGE22` |
