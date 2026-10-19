# Specification 020: one table per message type and a transfer status history

Status: proposed on codex/uat-certificates (base main 22a17ef) on 2026-10-19. The owner set the direction on 2026-10-19:

- **Columns (option C).** The original project's columns for each message, plus the fields needed for filtering and reports. The full request stays in the existing request JSON.
- **Coverage.** Every message type that moves through the gateway gets its own table.
- **History.** Only transfers keep a status history (outgoing payments and incoming credits).
- **Direction.** Incoming and outgoing share one table per message type, told apart by an enum column.

On 2026-10-19 the owner confirmed D1 to D5 as listed under [Decisions](#decisions). For D5 the owner added that the gateway needs a code model matching the IPS status codes.

## Why

Today a payment's business fields live only in request JSON (`Transactions.RequestJson`, `IncomingPayments.RequestJson`, `IncomingTransfers.RequestJson`). Its history lives only in `TransactionEvents.PayloadJson`. Neither can be filtered or reported with plain SQL. The original project (`IPS.MiidleWear.Persistence`, reference `d498de6`) kept the same information in typed tables:

| Original table | Content |
|---|---|
| `Pacs008Payments`, `Pacs009Payments`, `Pacs004Payments` | One row per message and direction (`Direction`), business fields as columns, and `Status` |
| `PaymentTransactions` | One row per transaction with its current status |
| `PaymentTransactionStatuses` | Append-only status history: `Status`, `Source`, `Step`, `ReasonCode`, `IpsInternalCode`, `Description`, `AtUtc` |

The rebuilt gateway already has the same status vocabulary: `TransactionStatus` (11 values), `StatusSource` and `ProcessingStep` are value-for-value identical to the original. Its `TransactionDirection` (`Incoming = 1`, `Outgoing = 2`) matches the original `PersistedPaymentDirection`.

## Scope

### Message tables

Each table holds both directions and has these common columns:

| Column | Type | Meaning |
|---|---|---|
| `Id` | `uniqueidentifier` | Primary key, equal to the aggregate id (`AggregateIdentities.Id`) |
| `Direction` | `int` (`TransactionDirection`) | `Incoming = 1`, `Outgoing = 2` |
| `Status` | `int` (`TransactionStatus`) | Current status, mapped for incoming messages as described [below](#incoming-status-mapping) |
| `StatusAtUtc` | `datetimeoffset` | Time of the current status |
| `ReasonCode`, `Description` | `nvarchar` | Current reason, from IPS or the core system |
| `ClientReference` | `nvarchar(35)` | Outgoing: the bank's reference. Incoming: null |
| `CoreReference` | `nvarchar` | The core system's reference, when known |
| `MessageId` | `nvarchar(35)` | Group header message id |
| `CreatedAtUtc` | `datetimeoffset` | Received from the bank, or received from IPS |

Columns per message type: the original project's columns where a table existed, and the identifying, amount and party fields otherwise.

| Table | Message | Directions | Specific columns |
|---|---|---|---|
| `Pacs008Payments` | pacs.008 | both | the original's: `InstructionId`, `EndToEndId`, `TransactionId`, `Uetr`, `Amount`, `Currency`, `ValueDate`, `AcceptanceDateTime`, `DebtorName`, `DebtorAccount`, `DebtorIdentification`, `DebtorAgentBic`, `CreditorName`, `CreditorAccount`, `CreditorIdentification`, `CreditorAgentBic`, `CategoryPurpose`, `Purpose`, `ChargeBearer`, `RemittanceInformation`, `FailureStage` |
| `Pacs009Payments` | pacs.009 | both | the original's: `InstructionId`, `EndToEndId`, `TransactionId`, `Uetr`, `Amount`, `Currency`, `ValueDate`, `DebtorAgentBic`, `DebtorAccount`, `CreditorAgentBic`, `CreditorAccount`, `CategoryPurpose`, `Purpose`, `RemittanceInformation` |
| `Pacs004Payments` | pacs.004 | both | the original's: `ReturnId`, `ReturnReasonCode`, `Amount`, `Currency`, `ValueDate`, `InstructedAgentBic`, `OriginalMessageId`, `OriginalMessageNameId`, `OriginalInstructionId`, `OriginalEndToEndId`, `OriginalTransactionId`, `OriginalUetr`, `AdditionalInformation` |
| `Pain001Initiations` | pain.001 | incoming | `PaymentInformationId`, `InstructionId`, `EndToEndId`, `Amount`, `Currency`, `RequestedExecutionDate`, `InitiatingPartyBic`, `DebtorName`, `DebtorAccount`, `DebtorAgentBic`, `CreditorName`, `CreditorAccount`, `CreditorAgentBic`, `CategoryPurpose`, `Purpose`, `RemittanceInformation` |
| `Camt056Recalls` | camt.056 | both | `RecallId`, `OriginalMessageId`, `OriginalEndToEndId`, `OriginalTransactionId`, `OriginalAmount`, `OriginalCurrency`, `OriginalSettlementDate`, `RecallReasonCode`, `DebtorName`, `DebtorAccount`, `DebtorAgentBic`, `CreditorName`, `CreditorAccount`, `CreditorAgentBic` |
| `Camt055Cancellations` | camt.055 | incoming | `CancellationId`, `PaymentCancellationId`, `AssignerBic`, `AssigneeBic`, `OriginalMessageId`, `OriginalPaymentInformationId`, `OriginalInstructionId`, `OriginalEndToEndId`, `Amount`, `Currency`, `RequestedExecutionDate`, `CancellationReasonCode`, `OriginatorBic`, `CreditorName`, `CreditorAccount` |
| `Camt029Resolutions` | camt.029 | both | `CancellationStatusId`, `OriginalMessageId`, `OriginalEndToEndId`, `OriginalTransactionId`, `ResolutionReasonCode`, `AdditionalInformation`, `Amount`, `Currency`, `SettlementDate` |
| `Pain002StatusReports` | pain.002 | outgoing | `OriginalMessageId`, `OriginalPaymentInformationId`, `ReportReasonCode`, `AdditionalInformation`, `OriginatorName` |
| `Pacs002StatusReports` | pacs.002 | both | `OriginalMessageId`, `OriginalEndToEndId`, `OriginalTransactionId`, `TransactionStatusCode` (ACCP/RJCT…), `ReportReasonCode`, `AdditionalInformation`. Incoming: IPS's unsolicited status reports. Outgoing: our replies to incoming pacs.008 |

Indexes on every table: `(Direction, CreatedAtUtc)`, `(Status, StatusAtUtc)`, the message's end-to-end or original end-to-end id, and `ClientReference` (filtered, non-null).

### Transfer status history

`PaymentTransactionStatuses` uses the original structure, plus direction and message type:

| Column | Type |
|---|---|
| `Id` | `bigint identity` |
| `PaymentTransactionId` | `uniqueidentifier`, the aggregate id |
| `Direction` | `int` (`TransactionDirection`) |
| `MessageType` | `varchar(35)`: pacs.008, pacs.009 or pacs.004 |
| `Status` | `int` (`TransactionStatus`) |
| `Source` | `int` (`StatusSource`) |
| `Step` | `int?` (`ProcessingStep`) |
| `ReasonCode`, `IpsInternalCode`, `Description` | as in the original |
| `AtUtc` | `datetimeoffset` |

Index: `(PaymentTransactionId, Id)`. Rows are append-only and are never updated or deleted.

### Incoming status mapping

Incoming aggregates record the core outcome and the IPS decision separately. Their table status and history use `TransactionStatus`:

| Incoming state | `Status` | `Source` |
|---|---|---|
| Registered from IPS | `Received` | Ips |
| Core submission started | `Sending` | Gateway |
| Core accepted, and for pacs.008 IPS accepted | `Accepted` | Core |
| Core rejected, or reply RJCT | `Rejected` | Core |
| Core outcome unknown, reconciliation running | `CoreUnknown` | Core |
| Manual review required | `ManualReview` | Gateway |

### How rows are written

- Rows are written **in the same transaction** as the state change that causes them, by a projection that the unit of work calls in step 3, next to `TransactionEvents` and `OutgoingStatusOutbox`. The original's write-behind queue is not reproduced: it could leave a table behind the real state and needed a dead-letter path.
- The unit of work stays orchestration only. The projection reads the tracked aggregates, their pending events and their metadata (request JSON). It inserts a message row when an aggregate is added and updates its current-status columns on each later state change.
- A history row is added for each status change of a pacs.008, pacs.009 or pacs.004 aggregate, in both directions.
- `TransactionEvents` stays as it is (ADR 0003). The new tables are queryable projections and are not the source of truth.

## Not in scope

- Workflow state tables (`Transactions`, `IncomingPayments`, `IncomingTransfers`), aggregates, Contracts, HTTP routes and wire shapes do not change.
- camt.053 and pcps.001 stay in `InboundMessageJournal` only. The gateway does not process them.
- No backfill. Following ADR 0003, disposable databases (including UAT) are recreated; only rows written after the migration appear.
- No reporting API. Reading the tables is plain SQL until a later specification adds one.
- The `IpsStatusCodes` reference table is not included. The catalog is a code model instead; see D5.

## Slices

| Slice | Content |
|---|---|
| **020a** | The IPS status code catalog (D5); `PaymentTransactionStatuses` for pacs.008 in both directions; the incoming status mapping; the projection hook in the unit of work |
| **020b** | `Pacs008Payments` in both directions; pacs.009 and pacs.004 added to the history |
| **020c** | `Pacs009Payments` and `Pacs004Payments` |
| **020d** | `Pain001Initiations`, `Camt056Recalls`, `Camt055Cancellations`, `Camt029Resolutions`, `Pain002StatusReports` and `Pacs002StatusReports` |

Each slice adds an EF Core migration generated with the EF CLI, SQL Server integration tests and an architecture update. Each is reviewed and merged before the next one starts.

## Acceptance scenarios

1. An outgoing pacs.008 accepted by IPS leaves one `Pacs008Payments` row (`Direction = Outgoing`, `Status = Accepted`, the amount, parties and end-to-end id as columns). Its `PaymentTransactionStatuses` rows read `Received → Sending → Accepted`, in order, with their sources and times.
2. An incoming pacs.008 credited by the core system leaves one `Pacs008Payments` row (`Direction = Incoming`, `Status = Accepted`). Its history reads `Received → Sending → Accepted`.
3. An incoming pacs.008 whose core call times out has history `Received → Sending → CoreUnknown`. When reconciliation later finds it credited, a reversal path or manual review is appended.
4. A rolled-back save (concurrency conflict) writes no message row and no history row.
5. A camt.056 sent and a camt.056 received both land in `Camt056Recalls`, distinguished by `Direction`.
6. Redelivery of an already stored incoming message adds no second row.

## Decisions

- **D1. Transfers.** Confirmed: pacs.008, pacs.009 and pacs.004 (everything that moves money), in both directions, keep a status history. pain.001, recalls, cancellations and status reports keep only their current status.
- **D2. "One table".** Confirmed: each new message table and the history table hold both directions. The existing workflow tables stay split, because merging them would rewrite the aggregates and their recovery logic.
- **D3. pacs.002.** Confirmed: included (`Pacs002StatusReports`), because it moves both ways. camt.053 and pcps.001 are excluded until the gateway processes them.
- **D4. Same-transaction writes.** Confirmed, instead of the original's write-behind queue.
- **D5. IPS status codes.** Confirmed: no `IpsStatusCodes` table. Instead, slice 020a ports the original catalog (`IpsStatusCode`, `IpsStatusCodes.All`: 47 codes from Annex D, Annex 2, each with an internal code, ISO code, description and action) into the gateway as a code model. History rows store `IpsInternalCode` as a number, and the catalog gives its ISO code, description and action.
