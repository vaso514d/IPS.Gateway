# Specification 017: versioned gateway routes, the core receiver package and its OpenAPI document

Status: on codex/core-receiver-sdk, stacked on codex/proxy-completion (c59ec3c, unmerged). Owner decisions of 2026-10-16 are recorded below; the owner resolved D1 (`net10.0`) and D2 (`2.0.0-preview.1`) the same day, which approved the specification. Slice 017a was committed by the owner as c78eff1. Slice 017b is implemented and uncommitted; see the implementation notes.

## Why

Every bank core system has to implement the endpoints the gateway calls (`IClientPaymentReceiver`): incoming payments, recalls, initiations, transaction statuses and the payment-status query. Today a bank receives only the C# interface, its XML comments and prose. It must build every endpoint by hand: routes, JSON options (requests are camelCase, `Pacs008PaymentResultDto` is PascalCase, the gateway posts `null` members), the 404 of the status query, and the answer rules the gateway applies. None of this is described in a formal document. The source `d498de6` had the same gap: its Swagger described only `IGatewayApi`, and the receiver existed only as a hand-mapped mock in `JsonGenerator.API/CoreReceiverEndpoints`.

## Owner decisions (2026-10-16)

1. **Bank systems are .NET only** (Azure, on-premises or Google Cloud; no .NET Framework). The primary client channel is our .NET packages; OpenAPI is the generated formal description, not the source of client code.
2. **Versioned routes from v1, on the gateway side only:** the routes the gateway serves move to `/api/v1/...`. The routes a bank implements (`IClientPaymentReceiver`) stay unversioned. No bank has integrated yet, so the break is made now. (Corrected by the owner on 2026-10-16: an earlier draft of 017a also versioned the bank routes.)
3. **New package name:** `IPS.MiidleWear.AspNetCore`.
4. **OpenAPI 3.0.**
5. **Branch:** codex/core-receiver-sdk on codex/proxy-completion (the integration branch codex/capability-rebuild named by the ledger does not exist in this repository; integration is main through pull requests).
6. **Plan extension.** The [rebuild plan](../rebuild-plan.md) excludes "standalone client/generator/mock hosts, or documentation tooling". The owner asked for the client package and the generated document, so this specification extends the plan for exactly these two deliverables. Request signing and certificates are a later specification; nothing here prepares for them beyond leaving room in the package.

## Scope

Two slices, reviewed and committed separately on this branch.

**017a: versioned gateway routes (Contracts 2.0.0).**
- The route constants of the gateway API gain the `/api/v1` prefix: every `*RestApiRoutes.Send`, `TransactionRestApiRoutes.Status` and the three `ProxyRestApiRoutes`. `/api/ips/.../send` becomes `/api/v1/ips/.../send`, and `/api/proxy/...` becomes `/api/v1/proxy/...`. Health endpoints are not versioned.
- The bank-side constants are unchanged: every `*RestApiRoutes.Receive`, `TransactionRestApiRoutes.ReceiveStatus` and `TransactionRestApiRoutes.PaymentStatus`. So are the CBS paths in `appsettings.json`.
- The Api controllers follow automatically, because they use the constants. Literal copies of the gateway routes are updated in `docs/configuration.md` and the tests.
- Package version becomes `2.0.0-preview.1` (D2).

**017b: the receiver package and its document.**
- `src/IPS.MiidleWear.AspNetCore`: maps a bank's `IClientPaymentReceiver` implementation to the nine endpoints.
- `docs/openapi/ips-core-receiver.json`: OpenAPI 3.0, generated from the same endpoint mapping.

Not in scope:
- The gateway's own API document (`*/send`, status, proxy) and a typed `IGatewayApi` client. These come next, in their own specification.
- Request signing, certificates and `securitySchemes`.
- An HTML reference page, package publication to a feed, and a conformance kit for banks.
- Any change to gateway behaviour, workflows, persistence or wire shapes beyond the route prefix.

## 017a design: versioned routes

- **Route constants.** Only the values of the gateway-side constants listed above change. Names, types, members and the bank-side values do not.
- **Documentation text** in Contracts that quotes a route is updated to the new value.
- **Compatibility exception.** `public-api.json` is edited by hand for exactly the changed constant values and the new package version. It is not regenerated from the changed assembly. Wire cases are unchanged, because no wire case contains a route. The baseline README and ADR 0002 record the exception.
- **Callers.** A bank system that calls the old `/api/ips/.../send`, `/api/ips/transactions/status` or `/api/proxy/...` paths gets 404 and must use the `/api/v1` paths. Nothing changes for the receiver it implements.

## 017b design: the receiver package

**Project.**
- `src/IPS.MiidleWear.AspNetCore`, packable, references `IPS.MiidleWear.Contracts` and the `Microsoft.AspNetCore.App` framework only. No other package.
- Target framework: `net10.0` (D1).
- The gateway does not reference it. The architecture rules allow it only Contracts.

**Registration.**
```csharp
builder.Services.AddIpsCoreReceiver<MyCoreReceiver>();   // scoped IClientPaymentReceiver
app.MapIpsCoreReceiver();                                 // the nine endpoints
```

**Mapping.**
- `MapIpsCoreReceiver` reads the `[RestEndpoint]` attribute of every `IClientPaymentReceiver` method, so no route is written twice. A method without the attribute stops the application at startup.
- Each endpoint gets its name, summary, description and tag from the attribute.
- **Request.** The body is read into the method's parameter type with the gateway's conventions: web defaults, names from `JsonPropertyName`, string enums, and `null` members accepted.
- **Answer.** A receive method's `Pacs008PaymentResultDto` is written with HTTP 200, PascalCase names as declared, and `null` members omitted.
- **Status query.** `GetPaymentStatusAsync` reads `messageKind` and `reference` from the query string. A `null` result answers 404.
- **Status receive.** `ReceiveTransactionStatusAsync` answers 204.
- **Failures.** Malformed JSON answers 400 ProblemDetails. An exception from the bank's implementation answers 500 ProblemDetails. The gateway treats both as "no answer" and asks again later, which is the safe outcome for a technical failure.
- **Idempotency-Key.** The interface methods have no header parameter, so the package exposes the key through a scoped `IIpsReceiveContext` (`IdempotencyKey`). This is new package surface; Contracts is not changed for it.
- **Cancellation.** `HttpContext.RequestAborted` is passed to every method.

**Contracts metadata for the document.**
- The receiver methods' `[RestEndpoint]` gain a `Description` with the rules the gateway applies to the answer:
  - only 2xx with `Status` `ACCP` or `RJCT` is final;
  - `Id`, `EndToEndId` and `TxId`, when returned, must equal the request;
  - a duplicate JSON member makes the answer unusable;
  - anything else is unknown and is followed by the status query;
  - 404 to the status query resubmits every kind except pacs.008, where it stays unknown and ends in manual review (ADR 0002, incoming reconciliation);
  - an RJCT is final, so a technical failure must not be answered with RJCT;
  - an incoming pacs.008 left unknown at the IPS deadline is rejected to IPS, and a later confirmed credit is reversed through `transactions/status/receive` (`direction` Incoming, `status` Rejected).
- The text follows the implemented interpreters (`IncomingCoreReplyInterpreter`, `IncomingTransferProcessing`, `IncomingReconciliation`, `OutgoingStatusDelivery`) and is checked against them during review.
- This is part of the same 2.0.0 exception and is hand-edited in `public-api.json`. The stale XML comment on `InboundPaymentStatusQueryDto.MessageKind` ("Pacs008, Pacs009 or Pacs004") is corrected to all seven delivered kinds.

**Contracts additions for implementers** (owner, 2026-10-16; base class library only, part of the 2.0.0 exception, each added explicitly to `public-api.json`):
- **`IpsJson.SerializerOptions`** is a read-only `System.Text.Json` instance with the gateway's wire conventions: web defaults, string enums through the existing attributes, and `null` members omitted when writing. A bank that serializes with other settings (for example Newtonsoft.Json, which ignores `JsonPropertyName` and `JsonStringEnumConverter`) can use it instead. The gateway's own serialization is not changed by this slice.
- **`Pacs008PaymentStatuses.Pending`** = `"PDNG"` and **`Pacs008PaymentResultDto.Pending(...)`**, next to `Accept` and `Reject`, for the status query while the core is still processing. The gateway reads `PDNG` as unknown, as today.
- **`IpsIdempotencyKeys`** returns the key the gateway sends for each delivered DTO, so a core system can check or derive it:
  - pacs.008 and pacs.009: `endToEndId`;
  - pacs.004: `id` (the return id);
  - pain.001: `paymentInformationId`;
  - camt.056 and camt.029: `id`;
  - camt.055: `msgId`;
  - a status of an outgoing transaction: `{clientReference}:{status}`;
  - a reversal of an incoming one: `in:{coreReference}:{status}`.

  `IpsHttpHeaders.IdempotencyKey` names the header once. A unit test pins every key against what the gateway's clients send.
- **XML documentation** on every public DTO property that lacks it, written from the gateway's mapping code and the Annex D field definitions. It reaches IntelliSense and the OpenAPI schemas.
- **Package readme.** `PackageReadmeFile` for Contracts and for `IPS.MiidleWear.AspNetCore`: the nine endpoints, the registration example, the answer rules and the key table.

**The document.**
- `tools/IPS.MiidleWear.OpenApi` is a console project. It is not shipped and not referenced by `src/`. It starts an in-process host with `MapIpsCoreReceiver` and `Microsoft.AspNetCore.OpenApi` (`OpenApiVersion` 3.0) and writes `docs/openapi/ips-core-receiver.json`.
- Because the document comes from the package's own endpoints, the package and the document cannot disagree.
- **Content:**
  - `info.version` = the Contracts package version;
  - `servers` = `https://{bank-host}` as a variable;
  - one operation per receiver method;
  - schemas from the Contracts DTOs, with property descriptions taken from the Contracts XML documentation file;
  - the `Idempotency-Key` header with its format per operation;
  - the 200, 204, 400, 404 and 500 answers;
  - examples taken from the existing wire cases.

## Acceptance

**017a**
- Every gateway-side route constant starts with `/api/v1/`, and every bank-side constant is unchanged. The Contracts test passes against the hand-edited baseline with only the listed changes. The wire cases are unchanged.
- The gateway serves `/api/v1/ips/*/send`, `/api/v1/ips/transactions/status` and `/api/v1/proxy/*`. The old paths answer 404.
- With default configuration the gateway still calls the bank at the unversioned `/api/ips/.../receive`, `/api/ips/payments/status` and `/api/ips/transactions/status/receive` paths. The integration, simulator and Aspire tests pass.

**017b**
- **Mapping.** `MapIpsCoreReceiver` maps exactly the nine interface methods at their attribute routes and methods, and fails at startup for a method without `[RestEndpoint]`.
- **Answers:**
  - a receive returns 200 with PascalCase names and no `null` members;
  - the status query returns 404 for `null`;
  - status receive returns 204;
  - malformed JSON returns 400;
  - an exception returns 500;
  - the Idempotency-Key reaches `IIpsReceiveContext`.
- **End to end against the gateway's real clients.**
  - The gateway's real `IncomingCbsClient`, `OutgoingStatusClient` and interpreters call a receiver built with the package on an in-process server.
  - ACCP and RJCT are final.
  - `PDNG`, 400 and 500 are unknown.
  - 404 to the status query follows the reconciliation rule of each kind.
  - A status receive is delivered.
  - A reversal notification reaches the receiver with its `in:` key.
- **Document:**
  - the committed document equals the generated one;
  - it is OpenAPI 3.0 and reads without errors with `Microsoft.OpenApi`;
  - it has exactly the nine operations;
  - every example deserializes into its DTO with unknown members rejected.
- **Contracts additions.**
  - `IpsJson.SerializerOptions` round-trips every wire case.
  - `Pending` produces `PDNG`.
  - `IpsIdempotencyKeys` equals the key the gateway sends for every kind, checked in the end-to-end test through `IIpsReceiveContext`.
  - Every public DTO property has documentation.
  - Both packages pack with their readme.
- **Architecture.** The dependency test admits `IPS.MiidleWear.AspNetCore` with the Contracts reference and the ASP.NET Core framework only. Contracts still references only the base class library.
- **Verification.** Release build, the full suite (`-m:1`), format, the EF check, `git diff --check`, and independent Standards and Spec reviews.

## Resolved decisions

- **D1. Target frameworks of `IPS.MiidleWear.AspNetCore`: `net10.0`** (owner, 2026-10-16). The alternatives were `net8.0;net10.0` and `net8.0` only.
- **D2. Contracts version label: `2.0.0-preview.1`** (owner, 2026-10-16). The alternative was `1.2.0-preview.1`.

## Risks

- **Old callers.** A bank system written against 1.1.0-preview.1 calls our old paths and gets 404 until it moves to `/api/v1`.
- **Second copy of the rules.** The response rules in the `Description` text are prose and can drift from the interpreters. The end-to-end tests pin the behaviour; the review checks the text.
- **Not verified with a real bank.** No bank has built a receiver yet, so the package's ergonomics are unproven until the first integration.

## Implementation notes (017b, 2026-10-16)

- **Echo rules differ by kind (correction).** The gateway compares the answer's `Id`, `EndToEndId` and `TxId` with what it knows, not with the delivered JSON:
  - **pacs.008** with the IPS group message id and transaction id, which the delivery does not carry. A pacs.008 answer must therefore leave `Id` and `TxId` empty; any value makes it unknown.
  - **pacs.009** with `id`, `endToEndId` and `txId`.
  - **pacs.004, pain.001 and the camt kinds** with `Id` only: the return id, `paymentInformationId`, or the Assgnmt/Id (`id`, or camt.055 `msgId`).

  The descriptions, the `Pacs008PaymentResultDto` documentation and both readmes state this per kind, and a regression case (`message-id`) pins it.
- **Property descriptions.** `Microsoft.AspNetCore.OpenApi` 10 reads the referenced Contracts XML documentation by itself, so the tool needs no schema transformer.
- **Proxy documentation.** The proxy DTOs are positional records, so they are documented with `<param>` tags.
- **Generator host.** The generator registers a description-only receiver that is never called. Its entry point is a named class, because .NET 10 makes a top-level `Program` public and it would clash with the Api's `Program` in the integration tests.
- **Baseline.** `public-api.json` was compared with a snapshot of the changed assembly line by line before each edit. Every differing line was a listed addition, and the receiver lines differ only inside `Description`.
- **Packaging.** `IPS.MiidleWear.AspNetCore` depends on `IPS.MiidleWear.Contracts` 2.0.0-preview.1, so a bank adds one package. Both pack with their readme (`dotnet pack`, checked by hand).
