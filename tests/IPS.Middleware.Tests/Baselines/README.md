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
