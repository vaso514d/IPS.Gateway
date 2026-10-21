# Specification 022: the incoming instant payment (pacs.008) contract with the core system

Status: implemented on main on 2026-10-21 and awaiting owner review (uncommitted). The owner supplied the contract on 2026-10-21. Field names follow the owner's JSON; lengths follow ISO 20022. It applies to pacs.008 only; the other six delivered kinds are unchanged.

## Owner decisions

1. The gateway's `transactionId` is the core system's correlation, idempotency, duplicate-protection and status-lookup key. The same transactionId is never a new payment.
2. The delivery is an envelope: `transactionId`, `operationType` (`INSTANT_PAYMENT`) and `data`, the business payload.
3. The payload adds `messageId` (GrpHdr/MsgId), `isoTransactionId` (PmtId/TxId) and `receivedAt`.
4. Accounts are `{ type, iban, treasuryCode }`: 0 is an IBAN, 1 a Treasury code (Annex D 3.2.1: …Acct/Id/Othr/Id is a Treasury code).
5. The answer is synchronous and final: ACCEPTED, or REJECTED with a reason. Anything else is UNKNOWN, never a rejection.

## Corrections to the supplied contract

| # | Supplied | Corrected | Why |
|---|---|---|---|
| 1 | "If the operation does not exist, the request may be retried" | The gateway never delivers a payment again. It asks the status query until the IPS deadline; a 404 ends in manual review | After the deadline IPS has been told RJCT and the money went back to the payer; a later delivery would credit a rejected payment |
| 2 | ACCEPTED carries only transactionId and status | `coreReference`, the posting, is added | The gateway needs it for reconciliation and reversal |
| 3 | No party type | `partyType` (0 organisation, 1 individual) on debtor, creditor and ultimate parties | The message carries OrgId or PrvtId; without it a tax code and a personal number cannot be told apart |
| 4 | Creditor without its indirect participant | `creditor.indirectParticipantBic` | Routing to this bank's indirect participant |
| 5 | Status query not defined | `GET /stp/incoming-payments/{transactionId}`: ACCEPTED, REJECTED, PENDING, or 404 | The recovery path for a lost answer |
| 6 | Reason unconstrained | `reasonCode` an ISO code (AC01, AC04, AM04…); `reasonDescription` at most 105 characters | They become the pacs.002 to IPS (Rsn/Cd, AddtlInf Max105Text) |
| 7 | No lengths | ISO lengths, below | |
| 8 | `creationDateTime` and `acceptanceDateTime` unmarked | REQUIRED | The gateway always sends them |

## Wire contract

`POST /stp/incoming-payments`, header `Idempotency-Key: <transactionId>`:

```json
{
  "transactionId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
  "operationType": "INSTANT_PAYMENT",
  "data": {
    "messageId": "BANKA202609250001234567",
    "instructionId": "BANK-PMT-20260925-000123",
    "endToEndId": "E2E-20260925-000123",
    "isoTransactionId": "TX-20260925-000123",
    "receivedAt": "2026-09-25T10:20:15.35+00:00",
    "creationDateTime": "2026-09-25T14:20:10.2+04:00",
    "acceptanceDateTime": "2026-09-25T14:20:10.7+04:00",
    "amount": 100.00,
    "currency": "GEL",
    "instructionPriority": "NORM",
    "categoryPurposeCode": "OTHR",
    "debtor": {
      "name": "გიორგი მაისურაძე",
      "partyType": 1,
      "identifier": "01001012345",
      "billIdentifier": null,
      "address": { "streetName": "რუსთაველის გამზირი", "buildingNumber": "10", "postCode": "0108", "townName": "თბილისი",
                   "countrySubdivision": "თბილისი", "country": "GE", "addressLines": "რუსთაველის გამზირი 10, თბილისი" },
      "participantBic": "BAGAGE22",
      "indirectParticipantBic": null,
      "account": { "type": 0, "iban": "GE29NB0000000101904917", "treasuryCode": null }
    },
    "creditor": {
      "name": "ნინო ბერიძე",
      "partyType": 1,
      "identifier": "01002012345",
      "address": null,
      "indirectParticipantBic": null,
      "account": { "type": 0, "iban": "GE95TB0000000123456789", "treasuryCode": null }
    },
    "ultimateDebtor": { "name": "Company X", "partyType": 0, "identifier": "123456789" },
    "ultimateCreditor": null,
    "paymentInitiation": { "channelCode": "MB", "geolocation": [ "41.7151,44.8271" ] },
    "initiationChannelInstrument": { "channelCode": "MOBL", "instrumentCodes": [ "PRXY" ], "electronicAddress": "41.7151,44.8271" },
    "remittance": {
      "unstructured": "Invoice 2026-000123 payment",
      "structured": [ { "referenceType": "SERV", "referenceIssuer": "SERVICE-PROVIDER", "reference": "ORDER-2026-000123",
                        "additionalInformation": "Additional payment information" } ]
    }
  }
}
```

Answer, HTTP 200:

```json
{ "transactionId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301", "status": "ACCEPTED", "coreReference": "POSTING-000123" }
{ "transactionId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301", "status": "REJECTED", "reasonCode": "AC04", "reasonDescription": "Account closed" }
```

`GET /stp/incoming-payments/{transactionId}` answers the same, `"status": "PENDING"` while still processing, or 404 when the payment never arrived.

## Fields and lengths

| Field | Required | Length / values |
|---|---|---|
| `transactionId` | yes | ≤ 64; the gateway sends its payment id (a GUID) |
| `operationType` | yes | `INSTANT_PAYMENT` |
| `messageId`, `instructionId`, `endToEndId`, `isoTransactionId` | yes | ≤ 35 |
| `receivedAt`, `creationDateTime`, `acceptanceDateTime` | yes | ISO 8601 with offset |
| `amount` | yes | 13 integer, 5 fractional digits |
| `currency` | yes | 3 letters |
| `instructionPriority` | no | NORM, HIGH |
| `categoryPurposeCode` | no | ≤ 4 |
| `debtor.name` | yes | ≤ 140 |
| `debtor.participantBic` | yes | BIC, 8 or 11 |
| `*.partyType` | with `identifier` | 0 organisation, 1 individual |
| `*.identifier`, `billIdentifier`, `indirectParticipantBic` | no | ≤ 35 |
| `creditor.name`, `ultimate*.name` | no | ≤ 140 |
| `account.type` | yes | 0 IBAN, 1 Treasury code |
| `account.iban` / `account.treasuryCode` | the one `type` names | ≤ 34 |
| `address.streetName`, `townName` | no | ≤ 140 |
| `address.buildingNumber`, `postCode` | no | ≤ 16 |
| `address.countrySubdivision` | no | ≤ 35 |
| `address.country` | no | 2 letters |
| `address.addressLines` | no | ≤ 490, sent as up to 7 lines of 70 |
| `remittance.unstructured` | no | split into 140-character parts |
| `reasonCode` | with REJECTED | ISO 20022 status reason, ≤ 4 |
| `reasonDescription` | no | ≤ 105 |
| `coreReference` | with ACCEPTED | the posting reference |

## Implementation

- **Contracts 3.0.0-preview.1** (breaking).
  - `IClientPaymentReceiver.ReceivePacs008Async` is replaced by `ReceiveIncomingPaymentAsync` and `GetIncomingPaymentAsync`.
  - New types: `IncomingPaymentRequestDto`, `IncomingInstantPaymentDto`, `IncomingDebtorDto`, `IncomingCreditorDto`, `IncomingAccountDto`, `IncomingUltimatePartyDto`, `IncomingPaymentResultDto`, and constants for the statuses, the operation type, the account types and the routes.
  - `Pacs008RestApiRoutes.Receive` is removed.
  - The baseline exception is recorded in `tests/IPS.Middleware.Tests/Baselines/README.md`.
- **Gateway.**
  - `IIncomingCoreClient` sends the stored snapshot and queries by the payment. `IncomingPaymentContract` maps it; its transactionId is the payment id.
  - The account type follows its bank, as on the send: a Treasury code when the bank is the Treasury (`Bank:TreasuryCode`, TRESGE22), an IBAN otherwise. It is never inferred from the form of the value (owner's Bank/Core contract, 2026-10-21).
  - `IncomingCoreReplyInterpreter` reads ACCEPTED and REJECTED for pacs.008, still ACCP and RJCT for the other kinds, and takes `reasonDescription` as the description.
  - `Payments:Incoming:Transport:SubmissionPath` and `IncomingPaymentStatusPath` hold the routes.
- **Receiver package, test kit, CoreAPI.** They map, check and answer the new contract.

## Not changed

- **The gateway's own identity of an incoming payment** stays the receiving participant and the EndToEndId.
- **The other six delivered kinds** are unchanged.

## Further owner decisions (2026-10-21)

- **Duplicate EndToEndId.** A different payment with the EndToEndId of one already registered is a duplicate. IPS is told RJCT with AM05 at once. The core system never sees it, and the registered payment is untouched. Before, the receipt was held without a reply, and IPS rejected the payment only at its deadline. A redelivery of the same payment (same contents) still joins the registered one.
- **IntrBkSttlmDt** of an outgoing pacs.008 is the UTC calendar date of acceptanceDateTime; before, it was the date in Georgian time (+04). Between 00:00 and 04:00 Georgian time the two differ: the date is then the previous day. Annex D 3.2.1 requires the current calendar day, with a 20-second grace after midnight for the previous one. If IPS checks the date in Georgian time, payments accepted in those four hours will be rejected. To be confirmed on UAT.
- **The outgoing contract stays as it is** (`POST /api/ips/pacs008/send`, flat), and so does `initiationChannelInstrument`.
