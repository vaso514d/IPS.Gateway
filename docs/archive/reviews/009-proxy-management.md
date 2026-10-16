# Review 009: proxy management (register, update, remove)

Branch codex/payment-initiation, stacked on 008c (f0e79c2) and the unmerged slices 005a-005d and 007a-007c; no merge is approved. On 2026-10-07 the owner chose a stateless rebuild as in the source, one slice for the three operations and no extras, then approved the [specification](../specs/009-proxy-management.md) with its two decisions (transport failures are 502/504; no certificate yet, so an unsigned message is sent as in the source). Commit and merge approval are pending.

## Delivered

- **Application `Proxy/`.** Request records, XSD-derived validators (IBAN and currency patterns, identifier and alias limits, holder type, XML-safe text), `ProxyManagement` (validate, prepare, one send, read the answer; a fresh operation and bulk reference per call) and the ports `IProxyProtocol` and `IProxyClient`.
- **Infrastructure `Proxy/`.** `Acmt022Message` (the three documents in the Annex E `hdr:Message` wrapper), the embedded `acmt.022.001.04` schema and `ProxySchema`, `ProxyProtocol` (shared ECDSA signer, or unsigned without a certificate), `ProxyReplyReader` (pacs.002.001.13 answer by local names, safe reader), `ProxyErrorCodes`, `ProxyClient` (one POST to `PRX/register|update|remove`, PRX headers, never retried; a timeout, other failure or non-success status is reported as no answer), `ProxySettings` and `ProxyTransportCertificates`. `TransportCertificates` gains an optional `signingOptional` flag.
- **Api.** `ProxyController` with the three routes of `ProxyRestApiRoutes`: accept and reject 200, validation 400, timeout 504, other failure 502 (problem details). The routes are removed while `Proxy:Enabled` is false. `Proxy` section in `appsettings.json`, disabled by default; `docs/configuration.md` and `docs/architecture.md` updated.
- No Contracts change, no migration, no storage.

## Changed existing tests

- None. `TransportCertificates` keeps its behavior for every existing caller (the new parameter defaults to false).

## New tests (65)

- **Unit (30):** every validation rule per operation reported at its field with nothing sent, update override and remove IBAN rules, one send per call with the operation id passed to the reader, no answer reported without a second attempt, fresh references, case-insensitive holder type.
- **Integration (35+):** schema-valid full, minimal, legal-entity, non-IBAN, update, empty-update, keep/remove-account and escaped-text documents with exact element order; Georgian text; unsigned without a certificate; signed with a certificate and verified independently by Java (and rejected after tampering); the reply reader (accept, reject, bulk reject, item by id, fallback, unreadable, empty, prefixed); the client against a simulated Proxy (route, headers, body, 307/400/429/500/503, timeout, unreachable, caller cancellation); the host (routes absent while disabled, OpenAPI operations, accept/reject/update, 400, minimal requests with only required fields, 502 and 504 without retry, invalid BIC stops startup).

## Verification

Build 0 warnings; 475 unit + 940 integration tests (1,415), all passing in the final run; `dotnet format` and the imports check clean; no pending EF model changes.

## Independent reviews

**Standards.** No layering violation. Fixed: a comment on the optional signing flag and on the hand-built protocol registration, the reply reader no longer throws to catch itself and uses the safe XML reader, the csproj item group, usings in the fixture, an architecture sentence. Recorded: the `signingOptional` flag and the small `ProxyTransportCertificates` wrapper (a shallower alternative was weighed and not taken: it would pass a policy that allows unsigned for every message); the disabled-transport guard mirrors the outgoing registration; the unreachable-host test accepts failure or timeout because a refused connection can outlast the connect timeout on Windows.

**Spec.** No behavioral divergence from the source in validation, element order, reply reading, routes or headers. Fixed: a blank account override in an update now means no account (the source sent an empty IBAN and the Proxy rejected it; here it would have been a 500 from the schema check); a Georgian-text assertion; minimal-request host tests (they show the missing-field concern about MVC model validation does not occur); the spec no longer lists a pacs.002 schema. Recorded in the spec notes: an unsigned message has no `Sgntr` (the source sent an empty one), the Channel header is upper-cased, the circuit breaker can return 502 without a call, signing failures surface as 500.

## Limits

What a real Proxy Solution accepts (alias formats, mandatory holder details, the optional `OrgnlPtyAndAcctId` on update, an unsigned message) rests on the source, its XSD and its mock; nothing is verified against a real Proxy Solution. A repeated call after an unknown outcome is not idempotent. Notification polling, lookup, inquiry, reachability, possession and payee verification are not rebuilt.
