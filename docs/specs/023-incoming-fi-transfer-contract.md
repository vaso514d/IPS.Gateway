# Specification 023: the incoming FI transfer (pacs.009) contract with the core system

Status: implemented on main on 2026-10-21 and awaiting owner review (uncommitted). The owner asked for the incoming pacs.009 to follow GE_IPS_Inception_Report_Annex_D_IPS-Participant_Interface v1.02 (15.06.26), with routes like the incoming pacs.008 (022). The pacs.009.001.11 XSD sets the field lengths. Only the incoming pacs.009 changes. The outgoing pacs.009 and the other five delivered kinds stay as they are.

## Owner decisions (2026-10-21)

1. **Envelope.** The delivery is an envelope like pacs.008's: `transactionId`, `operationType` (`FI_TRANSFER`) and `data`. The gateway's `transactionId` is the core system's key for correlation, idempotency, duplicate protection and the status query. The same transactionId is never a new operation.
2. **Routes like pacs.008.** `POST /stp/fi-transfers` and `GET /stp/fi-transfers/{transactionId}`.
3. **The XSD decides the lengths.** The routes and the DTO names carry no version.
4. **A CBS rejection is final for its transactionId.** If the core system rejects a delivered transfer, that transactionId is rejected for good. Whatever the core system does with the funds afterwards is a new operation under its own core reference. IPS has already settled the transfer and the gateway does not deliver it again.
5. **The outgoing pacs.009 is not changed.** `Id`, `ValueDate` and `DebtorAgent` stay mandatory.

## Asynchronous processing

IPS has already moved the funds between the technical accounts when it forwards a pacs.009 (Annex D 3.2.2, Fig. 10). The beneficiary gets a credit notification and does not accept or refuse it, so the gateway only acknowledges the message to IPS. The core system's answer decides only the core status of the transfer.

| Answer | HTTP | Meaning | Gateway |
|---|---|---|---|
| `ACCEPTED_FOR_PROCESSING` | 202 on the delivery | durably taken | not final: asks the status query on the backoff schedule |
| `PROCESSING` | 200 | being processed | not final |
| `COMPLETED` | 200 | processed, with `coreReference` | final: Accepted |
| `REJECTED` | 200 | refused, with `reasonCode` | final: Rejected (decision 4) |
| 404 on the status query | | the delivery never arrived | delivered again with the same transactionId |
| anything else, or another `transactionId` | | not an answer | not final |

An answer that does not echo the delivered `transactionId` is not an answer. An answer that omits `transactionId` is read as the answer to the call. After the 24-hour reconciliation window the transfer goes to manual review, like the other transfers.

## Wire contract

`POST /stp/fi-transfers`, header `Idempotency-Key: <transactionId>`:

```json
{
  "transactionId": "0f21e748-2d45-4bd6-8cf1-fd67711e5a10",
  "operationType": "FI_TRANSFER",
  "data": {
    "messageId": "MSG-IN-1",
    "instructionId": "INSTR-1",
    "endToEndId": "E2E-IN-1",
    "isoTransactionId": "TX-IN-1",
    "receivedAt": "2026-10-04T10:00:00+00:00",
    "amount": 100.5,
    "currency": "GEL",
    "settlementDate": "2026-10-04",
    "debtor": { "participantBic": "TBCBGE22", "directParticipantBic": null, "accountIban": "GE95TB0000000123456789" },
    "creditor": { "participantBic": "BAGAGE22", "accountIban": "GE29NB0000000101904917" },
    "categoryPurposeCode": "INTC",
    "purposeCode": "INTC",
    "remittanceInformation": "Liquidity transfer"
  }
}
```

Answer (202): `{ "transactionId": "…", "status": "ACCEPTED_FOR_PROCESSING" }`. The status query answers in the same shape, adding `coreReference` with COMPLETED and `reasonCode` and `reasonDescription` with REJECTED.

| Field | Required | Source | Length |
|---|---|---|---|
| `transactionId` | yes | the gateway's transfer id | GUID, ≤ 64 |
| `messageId` | yes | GrpHdr/MsgId | ≤ 35 |
| `instructionId` | IPS profile | PmtId/InstrId | ≤ 35 |
| `endToEndId` | yes | PmtId/EndToEndId | ≤ 35 |
| `isoTransactionId` | IPS profile | PmtId/TxId | ≤ 35 |
| `receivedAt` | yes | when the gateway registered the transfer | |
| `amount` | yes | IntrBkSttlmAmt | 18 digits, 5 fractional |
| `currency` | yes | IntrBkSttlmAmt/@Ccy | 3 letters |
| `settlementDate` | yes | IntrBkSttlmDt of the transaction, else of the group header | date |
| `debtor.participantBic` | yes | DbtrAgt BICFI (or Dbtr) | BIC, 8 or 11 |
| `debtor.directParticipantBic` | no | GrpHdr/InstgAgt BICFI, when IPS keeps it | BIC |
| `*.accountIban` | no | DbtrAcct / CdtrAcct Id/IBAN | ≤ 34 |
| `creditor.participantBic` | yes | CdtrAgt BICFI (or Cdtr) | BIC, 8 or 11 |
| `categoryPurposeCode` | no | PmtTpInf/CtgyPurp | Cd ≤ 4 |
| `purposeCode` | no | Purp | Cd ≤ 4, Prtry ≤ 35 |
| `remittanceInformation` | no | RmtInf/Ustrd, all lines joined | each line ≤ 140 |
| `reasonCode` | with REJECTED | | ISO 20022 status reason, ≤ 4 |
| `reasonDescription` | no | | ≤ 105 |
| `coreReference` | with COMPLETED | | the posting reference |

## Where Annex D is unclear or disagrees with the XSD

| # | Annex D | XSD / followed | Choice |
|---|---|---|---|
| 1 | 8.1.3 names the root `<FIToFICstmrCdtTrf>` | `FICdtTrf` | the XSD; the Annex looks copied from pacs.008 |
| 2 | Version written `pacs.009.001.011` | `pacs.009.001.11` | the XSD |
| 3 | InstrId and TxId [1..1] | optional | read as optional; sent when present |
| 4 | Ustrd [0..1] | unbounded | every line read and joined |
| 5 | DbtrAcct/CdtrAcct: IBAN only | IBAN or Othr | IBAN carried; Othr not mapped to the core |
| 6 | Purp listed without Cd/Prtry | choice of Cd or Prtry | both read |
| 7 | 3.2 says IPS removes GrpHdr/InstgAgt from forwarded messages, but also that the payload is copied as sent | InstgAgt optional | `directParticipantBic` optional, filled only when present |

To confirm with NBG on UAT: rows 3, 5 and 7.

## Implementation

- **Contracts 3.0.0-preview.1** (breaking; baseline exception 023 in `tests/IPS.Middleware.Tests/Baselines/README.md`).
  - `IClientPaymentReceiver.ReceivePacs009Async` is replaced by `ReceiveFiTransferAsync` (202) and `GetFiTransferAsync`.
  - New types `IncomingFiTransferRequestDto`, `IncomingFiTransferDto`, `IncomingFiTransferDebtorDto`, `IncomingFiTransferCreditorDto`, `IncomingFiTransferResultDto`, `IncomingFiTransferStatuses` and `IncomingFiTransferRestApiRoutes`, plus `IncomingOperationTypes.FiTransfer`.
  - `Pacs009RestApiRoutes.Receive` is removed. `IpsIdempotencyKeys.For(IncomingFiTransferRequestDto)` is the transactionId.
- **Gateway.**
  - `IIncomingTransferCoreClient` and `IIncomingTransferReplyInterpreter` take the registered `IncomingTransfer`, so a pacs.009 is delivered and queried by its id.
  - `IncomingFiTransferContract` (Infrastructure) builds the envelope. `IncomingPacs009` keeps GrpHdr/InstgAgt.
  - `IncomingCoreReplyInterpreter` reads COMPLETED and REJECTED for pacs.009.
  - `Payments:Incoming:Transport:Pacs009SubmissionPath` and `FiTransferStatusPath` hold the routes.
- **Receiver package, test kit, CoreAPI.**
  - The package maps both routes and documents the 202 answer.
  - The kit checks the answer, a redelivery and the status query.
  - CoreAPI answers COMPLETED, REJECTED or PROCESSING.

## Not changed

- **The outgoing pacs.009** (`POST /api/ips/pacs009/send`) and its validation.
- **The gateway's identity of an incoming transfer** stays the participant, the kind and the EndToEndId. The other five kinds keep their routes, DTOs and ACCP/RJCT.
