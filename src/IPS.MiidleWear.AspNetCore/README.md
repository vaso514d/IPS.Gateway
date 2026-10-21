# IPS.MiidleWear.AspNetCore

Hosts a core banking system's implementation of `IClientPaymentReceiver` (from `IPS.MiidleWear.Contracts`): the endpoints the IPS middleware (the gateway) calls to deliver incoming payments, recalls, initiations and transaction statuses, and to ask what happened to a delivery it got no answer for.

```csharp
builder.Services.AddIpsCoreReceiver<MyCoreReceiver>();   // MyCoreReceiver : IClientPaymentReceiver
var app = builder.Build();
app.MapIpsCoreReceiver();                                 // returns the endpoint group, e.g. for .RequireAuthorization()
```

The package reads every request and writes every answer with `IpsJson.SerializerOptions`, whatever JSON settings the application uses, and maps the routes from the contract.

## Authentication

To require the gateway's API token or signature on these endpoints, use `IPS.MiidleWear.Security.AspNetCore`. It requires what the `Server` section configures:

```csharp
builder.AddIpsServer();
var app = builder.Build();
app.MapIpsCoreReceiver().RequireIpsAuthentication();
```

## OpenAPI document

```csharp
builder.Services.AddIpsCoreReceiverOpenApi();            // the "ips-core-receiver" document, OpenAPI 3.0
var app = builder.Build();
app.MapIpsCoreReceiver();
app.MapOpenApi();                                         // serves /openapi/ips-core-receiver.json
```

The document lists only the receiver operations, with the `Idempotency-Key` header of each delivery and typed examples of what the gateway sends and reads. The application's own endpoints stay in its own documents.

## Endpoints

| Method | Route | Operation | Idempotency-Key |
|---|---|---|---|
| POST | `/stp/incoming-payments` | `ReceiveIncomingPaymentAsync` (pacs.008) | `transactionId` |
| GET | `/stp/incoming-payments/{transactionId}` | `GetIncomingPaymentAsync` (pacs.008) | none |
| POST | `/api/ips/pacs009/receive` | `ReceivePacs009Async` | `endToEndId` |
| POST | `/api/ips/pacs004/receive` | `ReceivePacs004Async` | `id` (the return id) |
| POST | `/api/ips/pain001/receive` | `ReceivePain001Async` | `paymentInformationId` |
| POST | `/api/ips/camt056/receive` | `ReceiveCamt056Async` | `id` |
| POST | `/api/ips/camt055/receive` | `ReceiveCamt055Async` | `msgId` |
| POST | `/api/ips/camt029/receive` | `ReceiveCamt029Async` | `id` |
| POST | `/api/ips/transactions/status/receive` | `ReceiveTransactionStatusAsync` | `{clientReference}:{status}`, or `in:{coreReference}:{status}` |
| GET | `/api/ips/payments/status?messageKind=&reference=` | `GetPaymentStatusAsync` | none; `reference` is the receive call's key |

`IpsIdempotencyKeys.For(message)` computes the key; `IIpsReceiveContext.IdempotencyKey` gives the header of the current call.

## What the package answers

| The receiver | HTTP answer |
|---|---|
| returns a `Pacs008PaymentResultDto` or an `IncomingPaymentResultDto` from a receive call or a status query | 200 with the result |
| returns `null` from `GetPaymentStatusAsync` or `GetIncomingPaymentAsync` | 404 |
| completes `ReceiveTransactionStatusAsync` | 204 |
| throws | 500 ProblemDetails; the exception is logged |
| (the body cannot be read) | 400 ProblemDetails |

## Rules for the receiver

- **The incoming instant payment (pacs.008)** has its own synchronous contract: the envelope `IncomingPaymentRequestDto` (`transactionId`, `operationType` INSTANT_PAYMENT, `data`). It is answered with `IncomingPaymentResultDto.Accepted(transactionId, coreReference)` or `Rejected(transactionId, reasonCode, reasonDescription)`; the reason code is an ISO 20022 code, and the description has at most 105 characters. Its status query answers `Pending(transactionId)` while processing. The `transactionId` is the key.
- **Final answers (the other kinds).** `Pacs008PaymentResultDto.Accept(...)` (ACCP) or `Reject(...)` (RJCT with a reason code) is final. Leave `Id`, `EndToEndId` and `TxId` `null` unless you echo them exactly as each operation's description says.
- **Technical failures.** RJCT is final, so never answer a technical failure (database down, timeout) with RJCT. Throw instead: the package answers 500 and the gateway asks again later.
- **Redelivery.** The same Idempotency-Key is the same message and must get the first answer, never a second posting.
- **Status query.**
  - Answer the stored result.
  - While the message is still being processed, answer PENDING (pacs.008) or `Pacs008PaymentResultDto.Pending(...)` (PDNG).
  - Answer `null` (404) only when the message never arrived. The gateway then delivers it again, for every kind except pacs.008.
- **pacs.008 deadline.** For an incoming pacs.008 the answer becomes the pacs.002 to IPS.
  - Without a final answer, the gateway asks the status query until the IPS deadline. It never delivers the payment again.
  - Still without a final answer, IPS is told RJCT. A credit the core system confirms later is reversed: the gateway posts the status callback with `direction` Incoming and `status` Rejected.

The full rules are in each operation's description in the OpenAPI document `ips-core-receiver.json`.
