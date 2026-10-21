# IPS.MiidleWear.Contracts

The JSON contract of the IPS middleware (the gateway) for a core banking system: the models, the routes and two interfaces.

- **`IGatewayApi`**: what the core system calls on the gateway, under `/api/v1`. Sends (`/api/v1/ips/{pacs008|pacs009|pacs004|camt056|camt029|pain002}/send`), the status of a sent transaction (`GET /api/v1/ips/transactions/status`), and proxy management (`/api/v1/proxy/{register|update|remove}`).
- **`IClientPaymentReceiver`**: what the core system implements and the gateway calls.
  - The incoming instant payment (pacs.008), synchronous: `POST /stp/incoming-payments` and its status query `GET /stp/incoming-payments/{transactionId}`.
  - Six receive calls (`/api/ips/{kind}/receive`).
  - The status callback (`/api/ips/transactions/status/receive`).
  - The payment-status query (`GET /api/ips/payments/status`).

To host `IClientPaymentReceiver` in ASP.NET Core, use the `IPS.MiidleWear.AspNetCore` package: it maps the routes, the JSON and the status codes.

## Helpers

| Helper | Use |
|---|---|
| `IpsJson.SerializerOptions` | The gateway's JSON conventions (property names from the attributes, string enums, `null` omitted when writing). Use them for the contract types instead of other serializer settings. |
| `IpsIdempotencyKeys.For(message)` | The `Idempotency-Key` the gateway sends with each delivery; also the `reference` of the payment-status query. |
| `IpsHttpHeaders.IdempotencyKey` | The header name. |
| `IncomingPaymentResultDto.Accepted` / `Rejected` / `Pending` | The pacs.008 answers: ACCEPTED with the posting, REJECTED with an ISO reason code, PENDING (status query only). |
| `Pacs008PaymentResultDto.Accept` / `Reject` / `Pending` | The other kinds' answers: ACCP, RJCT with a reason code, and PDNG (payment-status query only, while still processing). |

## Answer rules

Each `IClientPaymentReceiver` operation's `RestEndpoint` description states the rules the gateway applies to the answer:
- only 2xx with ACCEPTED or REJECTED (pacs.008), or ACCP or RJCT (the other kinds), is final;
- echoed ids must equal the delivered message exactly (for a pacs.008, only `transactionId`);
- RJCT is never the answer to a technical failure;
- a 404 to the status query means the message never arrived.
