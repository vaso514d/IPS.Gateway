# Contract compatibility baseline

Source repository: https://github.com/vaso514d/IPS.MiidleWear

Source commit: `d498de6c4638aa71cdb20189d13642b41abab5f1`.

The source Contracts files were exported from their raw Git blobs and built independently of the new repository. Expectations were captured from that original net8.0 assembly.

- `source-manifest.json`: original Git blob identities of every imported source/project file. This records the initial import provenance; it is not a test input or a constraint on later compatible source edits.
- `public-api.json`: exported types, constructors, members, constants, nullability, and attributes, including route metadata and enum values.
- `wire-cases.json`: representative JSON inputs with exact output captured from the original assembly using web serialization and null omission, matching the reference HTTP serializer configuration.

The original 15 wire cases (18 since 012c) exercise nested models, optional fields, property casing, dates/times, identifiers, status enums, callbacks, and proxy operations. Fixture deserialization rejects unknown fields so misspelled samples cannot silently lose coverage. This checks fixture quality; it does not change the production HTTP policy. The cases characterize serialization, not payment validation or protocol compliance.

`ContractSnapshot.cs` computes the actual public surface in tests using the normal Contracts project reference; source files are not linked or copied into the test output. There is intentionally no automatic baseline refresh step. An external contract or compatibility baseline change requires an approved compatibility decision with independently established expectations.

The original project file preserves its NuGet package metadata. The root configuration keeps it packable without publishing a package.

Approved exception (Stage 2c.2b, 2026-10-05): only IGatewayApi.SendPacs008Async's RestEndpoint success code changes from 202 to 200, with its description specifying final 200, unresolved 504 after the default 30-second wait, duplicate immediate 200 and reliable callbacks. The public-api.json entry was edited explicitly for this method; it was not regenerated from the changed assembly. Original source-manifest provenance and wire fixtures remain unchanged.

Approved exception (007a, 2026-10-06): only the RestEndpoint success code and description of IGatewayApi.SendPacs009Async, SendPacs004Async, SendCamt056Async and SendCamt029Async change, from 202 to 200, in the same wording as SendPacs008Async (final 200, unresolved 504, immediate duplicate 200, reliable callbacks; for the recall messages the final status is the technical verdict of IPS). The public-api.json entries were edited explicitly for these four methods, not regenerated from the changed assembly. Original source-manifest provenance and wire fixtures remain unchanged. SendPain002Async keeps 202 until it is built.

Approved exception (008b, 2026-10-07): only the RestEndpoint success code and description of IGatewayApi.SendPain002Async change, from 202 to 200, in the same wording as the other sends (final 200, unresolved 504, immediate duplicate 200; the final status is the answer of IPS to the refusal). The public-api.json entry was edited explicitly, not regenerated from the changed assembly. Original source-manifest provenance and wire fixtures remain unchanged. No IGatewayApi send now declares 202.

Approved exception (012c, 2026-10-08): an additive change, with the package version raised from 1.0.0-preview.1 to 1.1.0-preview.1. IClientPaymentReceiver gains ReceiveCamt056Async, ReceiveCamt055Async and ReceiveCamt029Async (POST, returning Pacs008PaymentResultDto like the other receive methods, with RestEndpoint metadata modelled on ReceivePain001Async); Camt056RestApiRoutes and Camt029RestApiRoutes gain a Receive constant; the new Camt055RestApiRoutes holds its Receive constant; IpsMessageKind gains Camt055 = 8, appended without renumbering. The existing camt.056, camt.055 and camt.029 DTOs are reused unchanged in shape; only their documentation gains the receive meaning. The public-api.json entries were added explicitly for exactly these members and types, not regenerated from the changed assembly. Three hand-written wire cases (a delivered camt.056, camt.055 and camt.029) were appended to wire-cases.json with independently written expectations; the original source-manifest provenance and the existing wire cases remain unchanged. Additive for callers, but not for implementers: a core system that implements IClientPaymentReceiver must add the three methods (and its endpoints) before it builds against 1.1.0-preview.1.

Approved exception (017a, 2026-10-16): a breaking change, with the package version raised from 1.1.0-preview.1 to 2.0.0-preview.1. The routes the gateway serves gain the /api/v1 prefix: every *RestApiRoutes.Send, TransactionRestApiRoutes.Status and the three ProxyRestApiRoutes constants, so also the RestEndpoint route arguments of IGatewayApi (/api/ips/.../send becomes /api/v1/ips/.../send, /api/proxy/... becomes /api/v1/proxy/...). The routes a core system implements (every Receive constant, ReceiveStatus, PaymentStatus and the IClientPaymentReceiver routes) are unchanged. No name, type, member, attribute property or other value changes. The public-api.json entries were edited explicitly for exactly these route strings, not regenerated from the changed assembly. No wire case contains a route, so wire-cases.json and the source-manifest provenance are unchanged. The owner chose the break before any core system implemented the contract; a core system built against 1.1.0-preview.1 must call the new /api/v1 paths; its receive endpoints stay where they are.

Approved exception (017b, 2026-10-16): additive, within the same 2.0.0-preview.1 version. The nine IClientPaymentReceiver RestEndpoint attributes gain a Description stating the rules the gateway applies to the answer; nothing else on them changes. The new namespace IPS.MiidleWear.Contracts.Http adds IpsJson (SerializerOptions), IpsHttpHeaders (IdempotencyKey) and IpsIdempotencyKeys (one For overload per delivered DTO and for TransactionStatusDto). Pacs008PaymentStatuses gains Pending = "PDNG" and Pacs008PaymentResultDto gains the static Pending factory. The public-api.json entries were edited for exactly these members: the expected entries were compared with a snapshot of the changed assembly line by line, every differing line was one of these additions (the nine method lines differ only by the Description), and only then were they written. XML documentation was added to every undocumented public property; documentation is not part of the baseline. Wire cases and the source-manifest provenance are unchanged.

Approved exception (022, 2026-10-21): a breaking change, with the package version raised from 2.0.0-preview.1 to 3.0.0-preview.1. The owner gave the incoming pacs.008 delivery its own synchronous contract, shared with the core system's team.
- `IClientPaymentReceiver.ReceivePacs008Async` (`POST /api/ips/pacs008/receive`, `Pacs008InstantPaymentRequestDto` → `Pacs008PaymentResultDto`) is replaced by:
  - `ReceiveIncomingPaymentAsync` (`POST /stp/incoming-payments`, `IncomingPaymentRequestDto` → `IncomingPaymentResultDto`);
  - `GetIncomingPaymentAsync` (`GET /stp/incoming-payments/{transactionId}`).
- `Pacs008RestApiRoutes.Receive` is removed.
- `IpsIdempotencyKeys.For` takes `IncomingPaymentRequestDto` (its `transactionId`) instead of `Pacs008InstantPaymentRequestDto`.
- `GetPaymentStatusAsync` changes only its description, which no longer covers pacs.008.
- Eleven types are added in `IPS.MiidleWear.Contracts.Pacs008`: the envelope, the payload, the debtor, creditor, account and ultimate-party DTOs, the result, and the status, operation-type, account-type and route constants.

The expected entries were compared with a snapshot of the changed assembly line by line, and every differing line was one of these changes; only then was public-api.json written. Wire cases and the source-manifest provenance are unchanged. The send contract (`Pacs008InstantPaymentRequestDto`, `IGatewayApi`) is unchanged.

Approved exception (023, 2026-10-21): breaking, within the same 3.0.0-preview.1 version (not yet published). The incoming pacs.009 delivery gets its own asynchronous contract, like pacs.008 in 022.
- `IClientPaymentReceiver.ReceivePacs009Async` (`POST /api/ips/pacs009/receive`) is replaced by:
  - `ReceiveFiTransferAsync` (`POST /stp/fi-transfers`, `IncomingFiTransferRequestDto` → `IncomingFiTransferResultDto`, success 202);
  - `GetFiTransferAsync` (`GET /stp/fi-transfers/{transactionId}`).
- `Pacs009RestApiRoutes.Receive` is removed; `IpsIdempotencyKeys.For` takes `IncomingFiTransferRequestDto` (its `transactionId`).
- `IncomingOperationTypes` gains `FiTransfer`; six types are added in `IPS.MiidleWear.Contracts.Pacs009` (the envelope, the payload, the debtor and creditor DTOs, the result, and the status and route constants).
- `GetPaymentStatusAsync` changes only its description.

The expected entries were compared with a snapshot of the changed assembly line by line; every differing line was one of these changes. The outgoing pacs.009 (`Pacs009PaymentRequestDto`, `IGatewayApi.SendPacs009Async`) is unchanged. Wire cases and the source-manifest provenance are unchanged.

Approved exception (unversioned gateway routes, 2026-10-21): a breaking change within the unpublished 3.0.0-preview.1. The owner removed the /api/v1 prefix of 017a from the routes the gateway serves: every *RestApiRoutes.Send, TransactionRestApiRoutes.Status and the three ProxyRestApiRoutes constants, and with them the RestEndpoint route arguments of IGatewayApi (/api/v1/ips/.../send becomes /api/ips/.../send, /api/v1/proxy/... becomes /api/proxy/...). The routes a core system implements are unchanged. The public-api.json entries were edited for exactly these route strings; no name, type, member or other value changes. No wire case contains a route, so wire-cases.json and the source-manifest provenance are unchanged.
