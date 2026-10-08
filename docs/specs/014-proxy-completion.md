# Specification 014: proxy completion (journal, queries, notifications)

Status: draft on codex/proxy-completion, base main (911cd96). On 2026-10-08 the owner asked to complete the Proxy Solution interface (Annex E v1.00), to store every operation and every incoming and outgoing XML in the database as the reference implementation `IPS.MiiddleWear` does (`PaymentTransactions` + `XmlDocuments`), and to expose the new operations in the API's Swagger. Awaiting the owner's decisions below; no code is written before they are approved.

## Why

Specification 009 rebuilt only Registration, Update and Removal, stateless. Annex E also defines Proxy Lookup, Account Holder Inquiry, Reachability Check, Account Possession Check, Verification of Payee (acmt.023 → acmt.024) and MMC notifications (`GET /PRX/notification`, `POST /PRX/notificationAck`). Lookup and Verification of Payee are what the proxy is for: resolving an alias to an account before a payment (out-of-flow, Annex E 1). Nothing the gateway sends to or receives from the Proxy Solution is recorded today, so an operator cannot see what was registered or why the Proxy refused it.

## Slices

Each slice is one review on this branch, in this order. Every slice adds its Swagger routes, its simulator behavior and its tests.

| Slice | Content |
|---|---|
| **014a** | Proxy journal (two tables), journaling of the existing register/update/remove, Proxy Lookup, Swagger UI |
| 014b | Verification of Payee |
| 014c | Reachability Check, Account Possession Check |
| 014d | Account Holder Inquiry (with the full-detail variant, see decision 8) |
| 014e | Read API over the journal: operations and XML/JSON documents |
| 014f | Bulk requests (several items per message) |
| 014g | Notification polling and acknowledgement worker |

This document details 014a; 014b–014g get a short addendum each before their implementation.

## 014a design

### Storage

Two new tables in the existing `TransactionDbContext`, one EF CLI migration:

- **`ProxyOperations`** — one row per call: `Id` (Guid), `Operation` (Register/Update/Remove/Lookup), `BulkMessageId`, `OperationId`, `Status` (Sending, Accepted, Rejected, NoAnswer), `ErrorCode`, `Description`, `DeliveryResult` (Replied/TimedOut/Failed), `RequestDocumentId`, `ReplyDocumentId`, `CreatedAtUtc`, `CompletedAtUtc`, `DurationMs`. Unique `OperationId`. For a lookup also `AliasType`, `Alias`, `Currency`, and on success `AccountIdentifier`, `ParticipantBic`.
- **`MessageDocuments`** — the XML/JSON store, as `XmlDocuments` in the reference: `Id` (Guid), `System` (Proxy now; IPS later, decision 3), `Direction` (Outgoing/Incoming), `MessageType` (acmt.022, acmt.023, acmt.024, pacs.002), `CorrelationId` (the operation id), `XmlContent` and `JsonContent` gzip-compressed `varbinary(max)` with original and stored sizes, `CreatedAtUtc`. Write-once.

The request JSON (as the core sent it), the signed outgoing XML and the Proxy's answer XML are all stored.

### Flow

1. Validate (unchanged for register/update/remove; new validator for lookup).
2. Build and sign the XML.
3. **Before sending:** save the operation row (`Sending`) and the request document (JSON + XML) in one `SaveChanges`. If that save fails, nothing is sent and the caller gets 503 (decision 4).
4. Send once (unchanged single-attempt client; new path `PRX/lookup`).
5. **After the answer:** save the reply document and the final status in one `SaveChanges`. If that save fails the caller still gets the Proxy's answer; the failure is logged and counted, and the row stays `Sending` so the operator sees an operation with an unrecorded outcome (decision 4).

No retry, no worker, no ownership: the call is synchronous, as today.

### Lookup (acmt.023 → acmt.024)

- Request: `aliasType` (MbNb, EmAd, MeId, IdNb), `alias` (≤128), `currency` (ISO 4217). Validation follows the acmt.023.001.04 XSD and the Annex E alias formats (mobile `+` and ≤15 digits; e-mail syntax).
- acmt.023: `Assgnmt` (fresh bulk id, our BIC as `Assgnr`, the Proxy BIC as `Assgne`), one `Vrfctn` with `Id` = fresh operation id, `PtyAndAcctId/Pty/CtctDtls/Othr` (`ChanlTp`, `Id`) and `Acct/Ccy`. Validated against the embedded `acmt.023.001.04.xsd` and signed like acmt.022.
- Answer: an acmt.024 is read (the report whose `OrgnlId` matches, else the first); a pacs.002 means a technical rejection and is read with the existing reader. The acmt.024 is validated against the embedded `acmt.024.001.04.xsd`; an unreadable or invalid answer is a reject `MS03`, as for acmt.022 answers.
- Result: `found` (`Vrfctn`), on success `accountIdentifier` + `isIban`, `currency`, `participantBic`, `holderName` (masked by the Proxy per its configuration; passed through unchanged), `aliasType`, `alias`; on failure `errorCode` and `description` from the error table (BE18 alias not found, AT07 invalid alias, FF01 …). Found and not found both return HTTP 200, like register/update/remove.

### API and Contracts

- `POST /api/proxy/lookup` → `ProxyLookupResultDto`, 400 for validation, 502/504 for no answer, 503 when the journal cannot be written.
- Contracts additions (decision 2): `ProxyRestApiRoutes.Lookup`, `ProxyLookupRequestDto`, `ProxyLookupResultDto`, `IGatewayApi.LookupProxyAsync`. Existing types and routes are unchanged.
- Swagger UI at `/swagger` over the existing `/openapi/v1.json` (decision 9).

### Configuration

`Proxy:Enabled=true` now requires `ConnectionStrings:Middleware` (server and database) and registers persistence. The host still never creates or migrates the database.

## Owner decisions requested

1. **Scope.** Lift the ledger's "not rebuild scope" for lookup, inquiry, reachability, possession, verification and notifications; the stateless rule of 009 is replaced by the journal. *Recommended: yes.*
2. **Contracts version.** New DTOs, routes and `IGatewayApi` methods make `IPS.MiidleWear.Contracts` 1.2.0 (additive minor, as 012c did); the public API baseline is updated for the additions only. *Recommended: yes.*
3. **One document store for all systems.** `MessageDocuments` has a `System` column so IPS messages can later be written there too (a separate slice; IPS keeps its current columns until then). The alternative is a proxy-only table. *Recommended: the shared table.*
4. **Journal failures.** Cannot record before sending → do not send, 503. Cannot record after the answer → return the answer anyway, log and count it. *Recommended as stated:* a registration that happened at the Proxy must not be reported as failed.
5. **What is stored.** The request JSON, the signed XML and the answer XML, in full, gzip-compressed. Proxy data includes personal data (names, personal ids); retention and access control are the operator's. *Recommended: store in full; a retention job is a later operational item.*
6. **Lookup currency mismatch.** Annex E rejects a lookup whose default account has another currency; the gateway passes the Proxy's answer through and adds no rule. *Recommended: pass through.*
7. **Bulk.** 014a–014e send one item per message; bulk is 014f. *Recommended: yes.*
8. **Inquiry FULL flag (014d).** Annex E puts `AddtlInf = FULL` in acmt.023, but the acmt.023.001.04 XSD has no `AddtlInf` in `IdentificationVerification5`; only message-level `SplmtryData` exists. Options: send FULL in `SplmtryData`, or always request the short form until Montran confirms. *Recommended: ask Montran; until then the short form.* Not needed for 014a.
9. **Swagger UI.** Add `Swashbuckle.AspNetCore.SwaggerUI` (UI only, over the existing Microsoft OpenAPI document), served in Development and, when `Api:SwaggerUi=true`, in other environments. *Recommended: yes.*
10. **Connection string name.** The code reads `ConnectionStrings:Middleware`; keep it (the local `IpsGateway` key must be renamed, and the password belongs in user secrets or an environment variable, not in the tracked `appsettings.json`). *Recommended: keep `Middleware`.*

## Acceptance (014a)

- Lookup validation: each field rule, each alias type format, unknown type.
- acmt.023: valid against the XSD for each alias type, exact element order, fresh ids, signed and independently verified (Java verifier), unsigned when no certificate is configured.
- acmt.024 reading: found, not found with each error code, matching report by `OrgnlId`, first-report fallback, pacs.002 technical reject, malformed and schema-invalid answers.
- Journal (SQL Server): register/update/remove/lookup each leave one operation row with request and reply documents; documents decompress to the exact bytes sent and received; save failure before send sends nothing and returns 503; save failure after the answer returns the answer and leaves `Sending`; duplicate operation id is impossible.
- Simulator: the test Proxy simulator answers lookup from registered data (ported from the reference `MockProxy`: `Acmt023Parser`, `Acmt024ResponseBuilder`, `ProxyDataStore`).
- API: 200 found and not found, 400, 502/504, 503; OpenAPI operation ids match the Contracts; Swagger UI serves in Development; host starts with the shipped (disabled) configuration and with Proxy enabled.
- Release build, full tests, format, EF model check, Contracts baseline updated only by the additions.

## Limits

Nothing is verified against a real Proxy Solution. The masked holder name, alias formats and error codes follow Annex E and the reference mock. A lookup is a read and is safe to repeat; a repeated register/update/remove after no answer may still be rejected as a duplicate.

## Source evidence

Annex E v1.00 §1.2.4, §2.3.3, §2.3.4, §3.5, Annex 1. Reference `IPS.MiiddleWear`: `Domain/Entities/XmlDocumentRecord.cs`, `Persistence/XmlDocumentStorage.cs`, `MockProxy/Acmt023Parser.cs`, `MockProxy/Acmt024ResponseBuilder.cs`, `MockProxy/Acmt023Endpoint.cs`, `Tests/Proxy/Schemas/acmt.023.001.04.xsd`, `acmt.024.001.04.xsd`.
