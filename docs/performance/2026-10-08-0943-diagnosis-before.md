# Outgoing payment performance, 2026-10-08 09:43 UTC

Measured with `tests/IPS.Middleware.Performance` as specified in [013](../specs/013-measured-performance.md), from 2026-10-08 09:43:10 to 09:49:16 UTC.

**Verdict: fail.** Not met: Settlement latency p95; Settlement latency p99; Lost payments (accepted, no final callback); Accepted payments not sent to IPS; Work due after the drain (ips.backlog.due). The evidence is below.

## Targets

| Target | Limit | Observed | Result |
|---|---|---|---|
| Settlement latency p95 | <= 1000 ms | 286442.5 ms (488 samples) | **fail** |
| Settlement latency p99 | <= 2000 ms | 291183.3 ms (488 samples) | **fail** |
| Lost payments (accepted, no final callback) | 0 | 8512 (without a callback 8512, callbacks but none final 0) | **fail** |
| Accepted payments not sent to IPS | 0 | 51 (never received by the simulated IPS 51, reported NotSent 0) | **fail** |
| Duplicate sends | 0 | 0 (flagged possible-duplicate resends of the same bytes, allowed: 0) | pass |
| Duplicate callbacks without an unknown delivery outcome before them | 0 | 0 (payments with more than one callback: 0) | pass |
| 5xx responses | 0 | 0 (504: 0; requests without a response: 0) | pass |
| Every response is 200 or 504 | 0 others | 0 others (200: 9600) | pass |
| Readiness Healthy throughout | every sample of every instance | 146 of 146 samples Healthy, 2 of 2 instances sampled | pass |
| Generator lag p99 | <= 50 ms | 14.8 ms (9000 samples) | pass |
| Work due after the drain (ips.backlog.due) | 0 | 8512 (outgoing_payments 0, incoming_payments 0, incoming_transfers 0, inbound_receipts 0, callbacks 8512) | **fail** |

## Latency (measured window, milliseconds)

Settlement runs from the generator's send to the simulated core's receipt of the payment's final callback; response from the send to the API's HTTP response; lag from a request's planned start to its actual start. Both ends are read from this machine's clock. Percentiles are exact (nearest rank over every sample); the verdict compares the unrounded values.

| | Samples | Min | Mean | p50 | p95 | p99 | Max |
|---|---|---|---|---|---|---|---|
| Settlement | 488 | 150203.4 | 221915.0 | 220797.0 | 286442.5 | 291183.3 | 291259.8 |
| Response | 9000 | 215.9 | 670.9 | 243.6 | 417.7 | 19415.4 | 24438.8 |
| Generator lag | 9000 | -1.8 | 7.0 | 6.9 | 13.9 | 14.8 | 35.7 |

## Throughput

- Planned: 50 requests per second; sent: 50.00 per second; callbacks received by the simulated core: 2.76 per second (over the measured window).
- Most requests open at once: 95, against 16 execution slots (2 instances at concurrency 8).
- 3966 requests started while at least 16 requests were open: their payments may have waited for admission, so the concurrency limit, not the code, can bound their latency (013 risk).

## Correctness (everything sent, warm-up included)

| Check | Count |
|---|---|
| Payments sent | 9600 |
| Responses by status code | 200: 9600 |
| Accepted by the service (200 or 504) | 9600 |
| 5xx responses (504 included) | 0 |
| 504 responses | 0 |
| Responses other than 200 or 504 (no response included) | 0 |
| Requests without a response | 0 |
| Accepted, not sent: never received by the simulated IPS | 51 |
| Accepted, not sent: reported NotSent to the core | 0 |
| Accepted, not sent (either of the two above) | 51 |
| Lost: accepted, without a callback | 8512 |
| Lost: accepted, callbacks but none final | 0 |
| Lost (either of the two above) | 8512 |
| Sent more than once other than as a flagged resend of the same bytes | 0 |
| Flagged possible-duplicate resends of the same bytes (allowed) | 0 |
| More than one callback | 0 |
| More than one callback and more callbacks than recorded delivery attempts (no unknown outcome before the repeat) | 0 |
| Investigations (pacs.028) received by the simulated IPS / payments investigated | 0 / 0 |
| Other IPS messages by definition | none |
| IPS messages / callbacks for no payment of the run or unreadable | 0 / 0 |

API answers by status code and body (the 10 most frequent):

| Answer | Count |
|---|---|
| 200 Accepted | 9549 |
| 200 NotSent TM01/1015 | 51 |

Final status the core was told, with reason code and IPS internal code:

| Final callback | Payments |
|---|---|
| no final callback | 8512 |
| Accepted | 1088 |

Examples, the first payments with each problem:

- accepted, not sent, lost: perf-4cb878f3742b: sent 09:46:18.376 to middleware-1, answered 200 NotSent TM01/1015, IPS sends 0 (flagged 0), investigations 0, callbacks [], deliveries [#3 Pending after 0 attempts]
- accepted, not sent, lost: perf-25aa1a5ab451: sent 09:46:18.437 to middleware-2, answered 200 NotSent TM01/1015, IPS sends 0 (flagged 0), investigations 0, callbacks [], deliveries [#3 Pending after 0 attempts]
- accepted, not sent, lost: perf-15a3affbc8da: sent 09:46:26.135 to middleware-1, answered 200 NotSent TM01/1015, IPS sends 0 (flagged 0), investigations 0, callbacks [], deliveries [#3 Pending after 0 attempts]
- accepted, not sent, lost: perf-09254d8ea3f8: sent 09:46:28.104 to middleware-1, answered 200 NotSent TM01/1015, IPS sends 0 (flagged 0), investigations 0, callbacks [], deliveries [#3 Pending after 0 attempts]
- accepted, not sent, lost: perf-8107bc0aad8c: sent 09:46:29.218 to middleware-1, answered 200 NotSent TM01/1015, IPS sends 0 (flagged 0), investigations 0, callbacks [], deliveries [#3 Pending after 0 attempts]
- lost: perf-693549d25c0b: sent 09:44:20.701 to middleware-1, answered 200 Accepted, IPS sends 1 (flagged 0), investigations 0, callbacks [], deliveries [#6 Pending after 0 attempts]
- lost: perf-7f4d9400ac8f: sent 09:44:20.717 to middleware-2, answered 200 Accepted, IPS sends 1 (flagged 0), investigations 0, callbacks [], deliveries [#6 Pending after 0 attempts]
- lost: perf-aeb0b377237f: sent 09:44:20.747 to middleware-1, answered 200 Accepted, IPS sends 1 (flagged 0), investigations 0, callbacks [], deliveries [#6 Pending after 0 attempts]
- lost: perf-47942bb78821: sent 09:44:20.763 to middleware-2, answered 200 Accepted, IPS sends 1 (flagged 0), investigations 0, callbacks [], deliveries [#6 Pending after 0 attempts]
- lost: perf-3643d27009b2: sent 09:44:20.779 to middleware-1, answered 200 Accepted, IPS sends 1 (flagged 0), investigations 0, callbacks [], deliveries [#6 Pending after 0 attempts]

## By instance (everything sent)

| Instance | Sent | Responses | Accepted, not sent | Lost | More than one callback | First failure (send time) |
|---|---|---|---|---|---|---|
| middleware-1 | 4800 | 200: 4800 | 26 | 4256 | 0 | 09:44:20.701 |
| middleware-2 | 4800 | 200: 4800 | 25 | 4256 | 0 | 09:44:20.717 |

## Timeline (by send time, one row per minute)

| From (UTC) | Phase | Sent | 200 | 504 | Other 5xx | Other | Not sent | More than one callback | Settlement p95 (ms) |
|---|---|---|---|---|---|---|---|---|---|
| 09:43:10 | WarmUp | 600 | 600 | 0 | 0 | 0 | 0 | 0 | 137095 |
| 09:44:10 | Measured | 3001 | 3001 | 0 | 0 | 0 | 0 | 0 | 286442 |
| 09:45:10 | Measured | 3000 | 3000 | 0 | 0 | 0 | 0 | 0 | 0 |
| 09:46:10 | Measured | 2999 | 2999 | 0 | 0 | 0 | 51 | 0 | 0 |

## API logs during the run

Warnings and errors each instance wrote from the start of the load to the end of the drain, from the orchestrator's log stream, by level (lower levels are skipped); then the most frequent by template (category and first message line, numbers and identifiers normalised, at most 80 characters) with the first such entry.

| Instance | Entries by level |
|---|---|
| middleware-1 | none |
| middleware-2 | none |

No warning or error was logged.

## Database diagnosis

Query Store captured every statement from the start of the load to the end of the drain. The costliest statements by total duration, with the plan each ran with most often (operators in tree order, with the table and index read); durations in milliseconds; failed executions were aborted (a command timeout or cancellation) or ended in an error; waits are Query Store's categories.

| Query | Executions (failed) | Total | Mean | Max | CPU | Mean logical reads | Waits | Statement | Plan |
|---|---|---|---|---|---|---|---|---|---|
| 9 | 64349 (0) | 42885 | 0.7 | 325 | 5012 | 9 | Lock 37835, Buffer Latch 30 | `(@reference nvarchar(35))SELECT TOP(2) [t].[Id], [t].[AcceptedJson], [t].[ClaimExpiresAtUtc], [t].[ClaimToken], [t].[Direction], [t].[MessageId], [t].[NextActionAtUtc], [t].[ProtocolTransactionId], [t].[RequestJson], [t].[RowVersion], [t].[UnsignedXml], [t0].[Id], [t0].[AggregateKind], [t0].[ClientReference], [t0].[CreatedAtUtc], [t0].[CurrentDescription], [t0].[CurrentIpsInternalCode], [t0].[CurrentReasonCode], [t0].[CurrentSequence], [t0].[CurrentSource], [t0].[CurrentStatus], [t0].[CurrentStatusAtUtc], [t0].[LastSequence], [t0].[MessageType], [t0].[RecallRefusedAtUtc], [t0].[RowVersion] FRO...` | Compute Scalar > Top > Nested Loops > Nested Loops > Index Seek [Transactions].[IX_Transactions_ClientReference] > Clustered Index Seek (lookup) [Transactions].[PK_Transactions] > Clustered Index Seek [Transactions].[PK_Transactions] |
| 5 | 1552 (0) | 14179 | 9.1 | 441 | 290 | 28 | Lock 13884, Memory 6, CPU 2 | `(@status int,@now datetimeoffset(7),@p int)SELECT TOP(@p) [t].[Id] FROM [Transactions] AS [t] LEFT JOIN [Transactions] AS [t0] ON [t].[Id] = [t0].[Id] WHERE [t0].[CurrentStatus] = @status AND [t].[ClaimToken] IS NULL AND ([t].[NextActionAtUtc] IS NULL OR [t].[NextActionAtUtc] <= @now) ORDER BY CASE WHEN [t0].[MessageType] = N'pacs.008' THEN 0 ELSE 1 END, [t0].[CurrentStatusAtUtc], [t].[Id]` | Top > Nested Loops > Sort > Compute Scalar > Index Seek [Transactions].[IX_Transactions_CurrentStatus_MessageType_CurrentStatusAtUtc_Id] > Clustered Index Seek [Transactions].[PK_Transactions] |
| 19 | 19098 (0) | 4412 | 0.2 | 56 | 4213 | 48 | Memory 468, Preemptive 191, Buffer IO 113 | `(@p0 uniqueidentifier,@p1 nvarchar(4000),@p2 datetimeoffset(7),@p3 int,@p4 int,@p5 nvarchar(2000),@p6 nvarchar(4000),@p7 int,@p8 uniqueidentifier,@p9 nvarchar(35),@p10 uniqueidentifier,@p11 uniqueidentifier,@p12 datetimeoffset(7),@p13 uniqueidentifier,@p14 datetimeoffset(7),@p15 int,@p16 uniqueidentifier)INSERT INTO [OutgoingMessages] ([Id], [Content], [CreatedAtUtc], [Direction], [Disposition], [Failure], [HeadersJson], [HttpStatusCode], [InvestigationId], [MessageDefinition], [OriginatingMessageId], [PaymentId], [ProcessedAtUtc], [ResendId], [StartedAtUtc], [Status], [SubmissionOwner]) VALUE...` | Assert > Nested Loops > Assert > Nested Loops > Nested Loops > Nested Loops > Assert > Clustered Index Insert > Compute Scalar > Compute Scalar > Constant Scan > Index Seek [OutgoingInvestigations].[AK_OutgoingInvestigations_Id_PaymentId] > Index Seek [OutgoingResends].[AK_OutgoingResends_Id_PaymentId] > Clustered Index Seek [Transactions].[PK_Transactions] > Index Seek [OutgoingMessages].[AK_OutgoingMessages_Id_PaymentId] |
| 7 | 9600 (0) | 3039 | 0.3 | 117 | 2671 | 50 | Memory 345, Preemptive 264, Latch 199 | `(@p2 uniqueidentifier,@p3 nvarchar(4000),@p4 datetimeoffset(7),@p5 uniqueidentifier,@p6 int,@p7 nvarchar(35),@p8 datetimeoffset(7),@p9 nvarchar(35),@p10 nvarchar(4000),@p11 nvarchar(4000),@p12 nvarchar(35),@p13 datetimeoffset(7),@p14 nvarchar(2000),@p15 int,@p16 nvarchar(35),@p17 int,@p18 int,@p19 int,@p20 datetimeoffset(7),@p21 int,@p22 nvarchar(16),@p23 datetimeoffset(7))INSERT INTO [Transactions] ([Id], [AcceptedJson], [ClaimExpiresAtUtc], [ClaimToken], [Direction], [MessageId], [NextActionAtUtc], [ProtocolTransactionId], [RequestJson], [UnsignedXml], [ClientReference], [CreatedAtUtc], [Cur...` | Compute Scalar > Assert > Nested Loops > Assert > Clustered Index Insert > Compute Scalar > Compute Scalar > Constant Scan > Index Seek [AggregateIdentities].[AK_AggregateIdentities_Id_Kind] |
| 22 | 19098 (0) | 2971 | 0.2 | 17 | 2965 | 16 | Memory 162, Preemptive 5 | `(@p15 uniqueidentifier,@p0 nvarchar(4000),@p1 datetimeoffset(7),@p2 int,@p3 int,@p4 nvarchar(2000),@p5 nvarchar(4000),@p6 int,@p7 uniqueidentifier,@p8 nvarchar(35),@p9 uniqueidentifier,@p10 datetimeoffset(7),@p11 uniqueidentifier,@p12 datetimeoffset(7),@p13 int,@p14 uniqueidentifier)UPDATE [OutgoingMessages] SET [Content] = @p0, [CreatedAtUtc] = @p1, [Direction] = @p2, [Disposition] = @p3, [Failure] = @p4, [HeadersJson] = @p5, [HttpStatusCode] = @p6, [InvestigationId] = @p7, [MessageDefinition] = @p8, [OriginatingMessageId] = @p9, [ProcessedAtUtc] = @p10, [ResendId] = @p11, [StartedAtUtc] = @p...` | Compute Scalar > Sequence > Table Spool > Split > Assert > Nested Loops > Nested Loops > Assert > Compute Scalar > Clustered Index Update > Compute Scalar > Compute Scalar > Compute Scalar > Clustered Index Seek [OutgoingMessages].[PK_OutgoingMessages] > Index Seek [OutgoingInvestigations].[AK_OutgoingInvestigations_Id_PaymentId] > Index Seek [OutgoingResends].[AK_OutgoingResends_Id_PaymentId] > Assert > Nested Loops > Table Spool > Index Seek [OutgoingMessages].[AK_OutgoingMessages_Id_PaymentId] > Filter > Table Spool |
| 8 | 28749 (0) | 2870 | 0.1 | 69 | 2670 | 20 | Preemptive 207, Buffer IO 119, Memory 94 | `(@p0 int,@p1 uniqueidentifier,@p2 varchar(32),@p3 uniqueidentifier,@p4 nvarchar(100),@p5 datetimeoffset(7),@p6 nvarchar(4000),@p7 int)INSERT INTO [TransactionEvents] ([Sequence], [TransactionId], [AggregateKind], [EventId], [Name], [OccurredAtUtc], [PayloadJson], [SchemaVersion]) VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7)` | Assert > Nested Loops > Assert > Clustered Index Insert > Index Seek [AggregateIdentities].[AK_AggregateIdentities_Id_Kind] |
| 11 | 9684 (0) | 2060 | 0.2 | 52 | 863 | 19 | Lock 997, Buffer Latch 164, Preemptive 27 | `(@p6 uniqueidentifier,@p7 varbinary(8),@p0 datetimeoffset(7),@p1 uniqueidentifier,@p2 int,@p3 int,@p4 datetimeoffset(7),@p5 int)UPDATE [Transactions] SET [ClaimExpiresAtUtc] = @p0, [ClaimToken] = @p1, [CurrentSequence] = @p2, [CurrentStatus] = @p3, [CurrentStatusAtUtc] = @p4, [LastSequence] = @p5 OUTPUT INSERTED.[RowVersion], INSERTED.[AggregateKind] WHERE [Id] = @p6 AND [RowVersion] = @p7` | Compute Scalar > Assert > Clustered Index Update > Compute Scalar > Compute Scalar > Top > Compute Scalar > Clustered Index Seek [Transactions].[PK_Transactions] |
| 10 | 29355 (0) | 1505 | 0.1 | 26 | 1015 | 6 | Lock 480 | `(@id uniqueidentifier)SELECT TOP(2) [t].[Id], [t].[AcceptedJson], [t].[ClaimExpiresAtUtc], [t].[ClaimToken], [t].[Direction], [t].[MessageId], [t].[NextActionAtUtc], [t].[ProtocolTransactionId], [t].[RequestJson], [t].[RowVersion], [t].[UnsignedXml], [t0].[Id], [t0].[AggregateKind], [t0].[ClientReference], [t0].[CreatedAtUtc], [t0].[CurrentDescription], [t0].[CurrentIpsInternalCode], [t0].[CurrentReasonCode], [t0].[CurrentSequence], [t0].[CurrentSource], [t0].[CurrentStatus], [t0].[CurrentStatusAtUtc], [t0].[LastSequence], [t0].[MessageType], [t0].[RecallRefusedAtUtc], [t0].[RowVersion] FROM [...` | Top > Nested Loops > Clustered Index Seek [Transactions].[PK_Transactions] > Compute Scalar > Clustered Index Seek [Transactions].[PK_Transactions] |
| 20 | 19098 (0) | 1413 | 0.1 | 51 | 1338 | 15 | Preemptive 111, Latch 50, Memory 19 | `(@p17 int,@p18 uniqueidentifier,@p19 varchar(32),@p20 uniqueidentifier,@p21 nvarchar(100),@p22 datetimeoffset(7),@p23 nvarchar(4000),@p24 int)INSERT INTO [TransactionEvents] ([Sequence], [TransactionId], [AggregateKind], [EventId], [Name], [OccurredAtUtc], [PayloadJson], [SchemaVersion]) VALUES (@p17, @p18, @p19, @p20, @p21, @p22, @p23, @p24)` | Assert > Nested Loops > Assert > Clustered Index Insert > Index Seek [AggregateIdentities].[AK_AggregateIdentities_Id_Kind] |
| 45 | 118 (0) | 1367 | 11.6 | 37 | 1325 | 9645 | Lock 41 | `(@p0 datetimeoffset(7))SELECT TOP(2) [s].[Value] FROM ( SELECT COUNT_BIG(*) AS [Value] FROM [Transactions] WHERE [NextActionAtUtc] <= @p0 ) AS [s]` | Top > Stream Aggregate > Clustered Index Scan [Transactions].[PK_Transactions] |
| 17 | 9549 (0) | 1187 | 0.1 | 17 | 1151 | 14 | Memory 153, Latch 16, Preemptive 13 | `(@p2 uniqueidentifier,@p3 varbinary(8),@p0 nvarchar(4000),@p1 int)UPDATE [Transactions] SET [UnsignedXml] = @p0, [LastSequence] = @p1 OUTPUT INSERTED.[RowVersion], INSERTED.[AggregateKind] WHERE [Id] = @p2 AND [RowVersion] = @p3` | Compute Scalar > Assert > Clustered Index Update > Compute Scalar > Compute Scalar > Clustered Index Seek [Transactions].[PK_Transactions] |
| 23 | 9549 (0) | 1174 | 0.1 | 51 | 982 | 17 | Buffer Latch 125, Latch 50, Preemptive 27 | `(@p9 uniqueidentifier,@p10 varbinary(8),@p0 datetimeoffset(7),@p1 uniqueidentifier,@p2 datetimeoffset(7),@p3 nvarchar(2000),@p4 int,@p5 int,@p6 int,@p7 datetimeoffset(7),@p8 int)UPDATE [Transactions] SET [ClaimExpiresAtUtc] = @p0, [ClaimToken] = @p1, [NextActionAtUtc] = @p2, [CurrentDescription] = @p3, [CurrentSequence] = @p4, [CurrentSource] = @p5, [CurrentStatus] = @p6, [CurrentStatusAtUtc] = @p7, [LastSequence] = @p8 OUTPUT INSERTED.[RowVersion], INSERTED.[AggregateKind] WHERE [Id] = @p9 AND [RowVersion] = @p10` | Compute Scalar > Assert > Clustered Index Update > Compute Scalar > Compute Scalar > Top > Compute Scalar > Clustered Index Seek [Transactions].[PK_Transactions] |
| 35 | 48 (0) | 1095 | 22.8 | 77 | 443 | 6028 | Lock 644, CPU 2 | `(@now datetimeoffset(7))SELECT COUNT_BIG(*) FROM [Transactions] AS [t] WHERE [t].[NextActionAtUtc] IS NOT NULL AND [t].[NextActionAtUtc] <= @now` | Stream Aggregate > Clustered Index Scan [Transactions].[PK_Transactions] |
| 18 | 19098 (0) | 975 | 0.1 | 50 | 857 | 4 | Latch 50, Buffer Latch 31, Preemptive 30 | `(@p2 uniqueidentifier,@p3 varbinary(8),@p0 datetimeoffset(7),@p1 int)UPDATE [Transactions] SET [NextActionAtUtc] = @p0, [LastSequence] = @p1 OUTPUT INSERTED.[RowVersion], INSERTED.[AggregateKind] WHERE [Id] = @p2 AND [RowVersion] = @p3` | Compute Scalar > Clustered Index Update > Compute Scalar > Clustered Index Seek [Transactions].[PK_Transactions] |
| 24 | 9549 (0) | 872 | 0.1 | 7 | 832 | 16 | Memory 44, Buffer IO 34, Buffer Latch 3 | `(@p16 uniqueidentifier,@p17 int,@p18 int,@p19 datetimeoffset(7),@p20 uniqueidentifier,@p21 datetimeoffset(7),@p22 nvarchar(2000),@p23 datetimeoffset(7),@p24 nvarchar(4000),@p25 int,@p26 int)INSERT INTO [OutgoingStatusDeliveries] ([PaymentId], [Sequence], [Attempts], [ClaimExpiresAtUtc], [ClaimToken], [DeliveredAtUtc], [LastFailure], [NextAtUtc], [PayloadJson], [PayloadVersion], [State]) OUTPUT INSERTED.[RowVersion] VALUES (@p16, @p17, @p18, @p19, @p20, @p21, @p22, @p23, @p24, @p25, @p26)` | Assert > Nested Loops > Assert > Clustered Index Insert > Clustered Index Seek [Transactions].[PK_Transactions] |

Server waits over the run (the container serves only this database), the largest first:

| Wait type | Waits | Milliseconds |
|---|---|---|
| SOS_WORK_DISPATCHER | 604738 | 13555589 |
| DISPATCHER_QUEUE_SEMAPHORE | 40 | 4497780 |
| SLEEP_TASK | 1233 | 919781 |
| LOGMGR_QUEUE | 75800 | 724991 |
| PREEMPTIVE_OS_FLUSHFILEBUFFERS | 71007 | 481785 |
| SQLTRACE_INCREMENTAL_FLUSH_SLEEP | 92 | 368011 |
| CHECKPOINT_QUEUE | 104 | 366083 |
| REQUEST_FOR_DEADLOCK_SEARCH | 73 | 365009 |
| LAZYWRITER_SLEEP | 385 | 364835 |
| HADR_FILESTREAM_IOMGR_IOCOMPLETION | 728 | 364351 |
| DIRTY_PAGE_POLL | 3636 | 364145 |
| XE_TIMER_EVENT | 95 | 363223 |

Deadlocks recorded by the system_health event files since the load started: 0. The JSON report holds every graph.

## Drain and backlog

The drain limit of 120 s ran out with 0 payments not final and 8512 callbacks pending.

Work due afterwards, by the query behind the `ips.backlog.due` gauge, read from the database (the service publishes the gauge only in its own process):

| Kind | Due |
|---|---|
| outgoing_payments | 0 |
| incoming_payments | 0 |
| incoming_transfers | 0 |
| inbound_receipts | 0 |
| callbacks | 8512 |

## Readiness (`/health/ready` every 5 s from warm-up to the end of the drain)

| Instance | Samples | Healthy | First | Last |
|---|---|---|---|---|
| middleware-1 | 73 | 73 | 09:43:10 | 09:49:10 |
| middleware-2 | 73 | 73 | 09:43:10 | 09:49:10 |

## Machine

- CPU: AMD Ryzen 7 7800X3D 8-Core Processor, 16 logical processors; memory 63.2 GiB.
- OS: Microsoft Windows 10.0.26200; .NET 10.0.12.
- Docker 29.6.2: 16 CPUs and 30.9 GiB for the containers.
- Containers running at the start of the load: k8s-course-control-plane (kindest/node:v1.31.0), sql-aundfwrr (mcr.microsoft.com/mssql/server:2022-latest).
- One machine ran SQL Server, the simulators, the API instances and the generator, so they competed for it (013 risk).

## Configuration

- 2 API instances run as projects, sharing one SQL Server database; requests round-robin over them.
- Warm-up: 60 s at 10 per second (not measured); measured: 180 s at 50 per second; drain limit 120 s.
- Simulated IPS: answers ACCP after 100 ms without verifying the service's signature; the simulated core takes every callback at once.
- Execution concurrency 8 per instance (shipped default 8; the Aspire tests use 4); the shipped timings of `appsettings.json`.
- Outgoing messages signed with a generated ECDSA P-256 key (`Payments:Signing:AllowUnsignedInDevelopment` off).

Settings the instances ran with that differ from `src/IPS.Middleware.Api/appsettings.json` (all injected by the AppHost as environment):

| Setting | appsettings.json | Used |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | (not set) | `Development` |
| `ConnectionStrings:Middleware` | (not set) | `(the AppHost's SQL Server container)` |
| `LOGGING:CONSOLE:FORMATTERNAME` | (not set) | `simple` |
| `Payments:Outgoing:Execution:Enabled` | `False` | `true` |
| `Payments:Outgoing:Policy:Currencies:0:Code` | (not set) | `GEL` |
| `Payments:Outgoing:Protocol:IpsBic` | (empty) | `NBGEGE22` |
| `Payments:Outgoing:Transport:Cbs:BaseUrl` | (empty) | `https://localhost:52729` |
| `Payments:Outgoing:Transport:Cbs:CheckCertificateRevocation` | `True` | `false` |
| `Payments:Outgoing:Transport:Cbs:ServerTrust:0:Path` | (not set) | `%TEMP%/ips-aspire-f28562bdbbd24737aaf27d2bbd4e15bc/trust/simulators-server.pem` |
| `Payments:Outgoing:Transport:Enabled` | `False` | `true` |
| `Payments:Outgoing:Transport:Ips:BaseUrl` | (empty) | `https://localhost:52729` |
| `Payments:Outgoing:Transport:Ips:CheckCertificateRevocation` | `True` | `false` |
| `Payments:Outgoing:Transport:Ips:ServerTrust:0:Path` | (not set) | `%TEMP%/ips-aspire-f28562bdbbd24737aaf27d2bbd4e15bc/trust/simulators-server.pem` |
| `Payments:Outgoing:Transport:IpsSignatureTrust:0:Path` | (not set) | `%TEMP%/ips-aspire-f28562bdbbd24737aaf27d2bbd4e15bc/trust/ips-signing.pem` |
| `Payments:Outgoing:Transport:IpsVersion` | `1` | middleware-1 `1`, middleware-2 `2` |
| `Payments:Outgoing:Transport:ParticipantBic` | (empty) | `BAGAGE22` |
| `Payments:Outgoing:Transport:SigningCertificate:Password` | (not set) | `(generated)` |
| `Payments:Outgoing:Transport:SigningCertificate:Path` | (not set) | `%TEMP%/ips-aspire-f28562bdbbd24737aaf27d2bbd4e15bc/signing/outgoing-signing.pfx` |
| `Proxy:Enabled` | `False` | `true` |
| `Proxy:Endpoint:BaseUrl` | (empty) | `https://localhost:52729` |
| `Proxy:Endpoint:CheckCertificateRevocation` | `True` | `false` |
| `Proxy:Endpoint:ServerTrust:0:Path` | (not set) | `%TEMP%/ips-aspire-f28562bdbbd24737aaf27d2bbd4e15bc/trust/simulators-server.pem` |
| `Proxy:ParticipantBic` | (empty) | `BAGAGE22` |
| `Proxy:ProxyBic` | (empty) | `PROXGE22` |
