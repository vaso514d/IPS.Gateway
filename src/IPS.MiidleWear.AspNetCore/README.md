# IPS.MiidleWear.AspNetCore

Hosts a core banking system's implementation of `IClientPaymentReceiver` (from `IPS.MiidleWear.Contracts`): the endpoints the IPS middleware (the gateway) calls to deliver incoming payments, recalls, initiations and transaction statuses, and to ask what happened to a delivery it got no answer for.

```csharp
builder.Services.AddIpsCoreReceiver<MyCoreReceiver>();   // MyCoreReceiver : IClientPaymentReceiver
var app = builder.Build();
app.MapIpsCoreReceiver();                                 // returns the endpoint group, e.g. for .RequireAuthorization()
```

The package reads every request and writes every answer with `IpsJson.SerializerOptions`, whatever JSON settings the application uses, and maps the routes from the contract.

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
| POST | `/api/ips/pacs008/receive` | `ReceivePacs008Async` | `endToEndId` |
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
| returns a `Pacs008PaymentResultDto` from a receive call or the status query | 200 with the result |
| returns `null` from `GetPaymentStatusAsync` | 404 |
| completes `ReceiveTransactionStatusAsync` | 204 |
| throws | 500 ProblemDetails; the exception is logged |
| (the body cannot be read) | 400 ProblemDetails |

## Rules for the receiver

- **Final answers.** `Pacs008PaymentResultDto.Accept(...)` (ACCP) or `Reject(...)` (RJCT with a reason code) is final. Leave `Id`, `EndToEndId` and `TxId` `null` unless you echo them exactly as each operation's description says. For a pacs.008, `Id` and `TxId` must stay `null`: the delivery carries neither, and any value makes the answer unusable.
- **Technical failures.** RJCT is final, so never answer a technical failure (database down, timeout) with RJCT. Throw instead: the package answers 500 and the gateway asks again later.
- **Redelivery.** The same Idempotency-Key is the same message and must get the first answer, never a second posting.
- **Status query.**
  - Answer the stored result.
  - While the message is still being processed, answer `Pacs008PaymentResultDto.Pending(...)` (PDNG).
  - Answer `null` (404) only when the message never arrived. The gateway then delivers it again, for every kind except pacs.008.
- **pacs.008 deadline.** For an incoming pacs.008 the answer becomes the pacs.002 to IPS. Without a final answer before the IPS deadline, IPS is told RJCT. A credit the core system confirms later is reversed: the gateway posts the status callback with `direction` Incoming and `status` Rejected.

The full rules are in each operation's description in the OpenAPI document `ips-core-receiver.json`.
