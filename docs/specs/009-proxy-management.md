# Specification 009: proxy management (register, update, remove)

Status: specification on codex/payment-initiation, stacked on 008c (f0e79c2) and the earlier unmerged slices 005a-005d and 007a-007c (merge approval pending for all of them). The owner chose, on 2026-10-07, a stateless rebuild as in the source, one slice for all three operations, and no extras. The owner approved the specification and the decisions below on 2026-10-07; implemented, review pending.

## Scope

The three public Proxy Solution operations of Contracts `IGatewayApi`: `RegisterProxyAsync` (`POST /api/proxy/register`), `UpdateProxyAsync` (`/api/proxy/update`) and `RemoveProxyAsync` (`/api/proxy/remove`), Annex E 1.2.1 and 1.2.2. Each request is validated, mapped to an `acmt.022.001.04` document, signed, POSTed to the Proxy Solution and its `pacs.002` reply is read into `ProxyOperationResultDto` (accepted, or rejected with the Proxy error code and a description). Accept and reject both return HTTP 200, as the Contracts already declare. The Proxy interface is a separate system from IPS: its own base URL, BIC, headers and timeouts.

Not in scope: notification polling (`GET /PRX/notification` and its acknowledgement), lookup, inquiry, reachability, possession and payee verification (the ledger records them as not rebuild scope), any storage, worker, retry or status query, and delivering anything to the core. No Contracts change (the proxy DTOs, routes and `IGatewayApi` declarations are already imported unchanged) and no migration.

## Source evidence

Pinned source d498de6c4638aa71cdb20189d13642b41abab5f1: `API/Proxy/Endpoints/ProxyManagementApi.cs` (build, sign when a certificate is loaded, send, interpret; a GUID for the operation id and for the bulk message id), `API/Proxy/Domain/Iso20022/Acmt022XmlBuilder.cs` (355 lines; the XSD is authoritative where it and the PDF disagree; Update sends `OrgnlPtyAndAcctId`; Remove sends `UpdtdPtyAndAcctId` with an account only when the account stays active), `API/Proxy/Domain/Iso20022/Pacs002V13Reader.cs`, `API/Proxy/Domain/ErrorCodes/ProxyErrorCodes.cs`, `API/Proxy/Validation/ProxyRequestValidators.cs` (only the limits the XSD enforces; business rules belong to the Proxy Solution and come back as a reject), `API/Proxy/Options/ProxyStpOptions.cs` and `Transport/ProxyHttpClient.cs` + `ProxyHttpHeaders.cs` (one POST per operation to `PRX/register`, `PRX/update`, `PRX/remove`; headers `X-MONTRAN-PRX-Channel` = our BIC and `X-MONTRAN-PRX-Version`; the same certificate as IPS may be reused; `EnsureSuccessStatusCode`), `Tests/Proxy/*` and `Tests/Proxy/Schemas/acmt.022.001.04.xsd`.

## Design

Follows the layering of the other capabilities, with no state.

- **Application `Payments`-independent `Proxy/`:** `ProxyRegisterRequest`/`ProxyUpdateRequest`/`ProxyRemoveRequest` are the Contracts DTOs themselves (they are the public types; no parallel copies), a validator per operation with the source's XSD-derived limits (IBAN and currency patterns, 256-character party identifiers, 34-character other account ids, 35-character account type, 4-character channel type, 128-character alias, XML-safe text, holder type `Individual` or `LegalEntity`), and `ProxyManagement` which runs validate, build, send, read and returns `ProxyOperationResultDto`. Ports: `IProxyMessageProtocol` (build and sign) and `IProxyClient` (send).
- **Infrastructure `Proxy/`:** `Acmt022Builder` (the three documents, element order as the XSD), the embedded `acmt.022.001.04` schema (the `pacs.002.001.13` answer is read, not schema-validated, as in the source, so no schema is embedded for it), `ProxyPacs002Reader`, `ProxyErrorCodes`, `ProxyHttpClient` (typed `HttpClient`, the three routes, the PRX headers, request timeout) and `ProxyOptions` (`Proxy` configuration section: base URL, participant BIC, Proxy BIC, protocol version, signing certificate, timeouts, connection pool), validated at startup and disabled by default like the other transports. Signing reuses the existing message-agnostic XML signer.
- **Api:** one `ProxyController` with three routes taken from `ProxyRestApiRoutes`, mapping validation errors to 400 problem details and the result to 200.
- **Ids.** A new GUID (no dashes) is the operation id and another the bulk message id on every call, as in the source: operation ids must contain no spaces and be unique in the Proxy's 24-hour dedup window.
- **Reading the reply.** A bulk-level reject returns its code and description; otherwise the item whose operation id matches (or the first item) decides; an unparsable reply or no item is a reject with code `MS03`, as the source does. Descriptions fall back to the error-code table.

## Owner decisions (2026-10-07)

1. **Transport failures return 502 or 504, as recommended.** A timeout is 504 and a connection failure or non-2xx from the Proxy is 502, both with a problem-details body and never a fabricated accept or reject. The endpoint description says the outcome is then unknown and that a retry may be rejected by the Proxy as a duplicate.
2. **Certificate: as the source.** There is no certificate yet, so the document is signed only when a signing certificate is configured and sent unsigned otherwise; enabling the section does not require one. Requiring one is a later change once a certificate exists.

## Acceptance

- Validation: every rule above for each operation, including the update override rules (an `AccountIdentifier` override must be an IBAN when no `UpdatedAccount` is given) and the remove IBAN requirement; errors report the failing field.
- XML: each document is valid against the embedded `acmt.022.001.04` schema for full and minimal requests (optional account, identifiers, persons, owners absent; Georgian text; remove with and without keeping the account), has the exact element order, uses the fresh operation and bulk ids, is signed and the signature verifies independently (Java verifier).
- Reply reading: accepted, bulk reject, item reject by operation id, first-item fallback, no item, malformed XML, a known and an unknown error code.
- Transport: the right route, headers and content type per operation; timeout, connection failure and non-2xx map to the decision above; no retry; no state is written.
- API: 200 for accept and for reject, 400 for validation, the transport result, and OpenAPI route names and operation ids matching the Contracts declarations; the host starts with the shipped configuration (disabled) and with the section enabled.
- The public API baseline is unchanged; no existing test is changed beyond the mechanical host and OpenAPI path counts; build, full tests, format, EF model unchanged, independent Standards and Spec reviews.

## Limits

What a real Proxy Solution accepts (alias formats, mandatory holder details, the optional `OrgnlPtyAndAcctId` on update) rests on the source, its XSD and its mock; nothing is verified against a real Proxy Solution. A repeated call after an unknown outcome is not idempotent.

## Implementation notes

- The Application layer does not reference Contracts, so it defines its own request records (`RegisterProxyRequest`, `UpdateProxyRequest`, `RemoveProxyRequest` and their parts) and `ProxyRequestMapping` in the Api maps the imported DTOs to them, as for the payment requests. The ports are `IProxyProtocol` (prepare the three operations, read the reply) and `IProxyClient`.
- Without a certificate the message is validated and sent unsigned (decision 2) on the Proxy's own `ProxyTransportCertificates`, built from the shared `TransportCertificates` with signing made optional; the IPS signing policy is untouched.
- The source writes `BfyOwnr` for beneficial owners in a removal but `Bnfcry` in registration and update; the builder keeps this as the source sends it.
- A signing failure (a configured certificate that is unusable) is not a transport result and surfaces as an unhandled error (HTTP 500), as in the source.
- The routes are removed by an application-model convention while `Proxy:Enabled` is false, like the outgoing controller.
- A blank account override in an update means no account, so an empty IBAN is never sent (the source sent it and the Proxy rejected it).
- An unsigned message has no `Sgntr` element (the source sent an empty one); the signer adds it when signing. The Channel header is the upper-cased BIC, as for IPS.
- The single-attempt HTTP client keeps its circuit breaker: after repeated failures calls return 502 without being attempted until it closes. The answer is read with the safe XML reader (no DTDs).
