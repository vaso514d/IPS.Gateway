# Contract compatibility baseline

Source repository: https://github.com/vaso514d/IPS.MiidleWear

Source commit: `d498de6c4638aa71cdb20189d13642b41abab5f1`.

The source Contracts files were exported from their raw Git blobs and built independently of the new repository. Expectations were captured from that original net8.0 assembly.

- `source-manifest.json`: original Git blob identities of every imported source/project file. This records the initial import provenance; it is not a test input or a constraint on later compatible source edits.
- `public-api.json`: exported types, constructors, members, constants, nullability, and attributes, including route metadata and enum values.
- `wire-cases.json`: representative JSON inputs with exact output captured from the original assembly using web serialization and null omission, matching the reference HTTP serializer configuration.

The 15 wire cases exercise nested models, optional fields, property casing, dates/times, identifiers, status enums, callbacks, and proxy operations. Fixture deserialization rejects unknown fields so misspelled samples cannot silently lose coverage. This checks fixture quality; it does not change the production HTTP policy. The cases characterize serialization, not payment validation or protocol compliance.

`ContractSnapshot.cs` computes the actual public surface in tests using the normal Contracts project reference; source files are not linked or copied into the test output. There is intentionally no automatic baseline refresh step. An external contract or compatibility baseline change requires an approved compatibility decision with independently established expectations.

The original project file preserves its NuGet package metadata. The root configuration keeps it packable without publishing a package.

Approved exception (Stage 2c.2b, 2026-10-05): only IGatewayApi.SendPacs008Async's RestEndpoint success code changes from 202 to 200, with its description specifying final 200, unresolved 504 after the default 30-second wait, duplicate immediate 200 and reliable callbacks. The public-api.json entry was edited explicitly for this method; it was not regenerated from the changed assembly. Original source-manifest provenance and wire fixtures remain unchanged.

Approved exception (007a, 2026-10-06): only the RestEndpoint success code and description of IGatewayApi.SendPacs009Async, SendPacs004Async, SendCamt056Async and SendCamt029Async change, from 202 to 200, in the same wording as SendPacs008Async (final 200, unresolved 504, immediate duplicate 200, reliable callbacks; for the recall messages the final status is the technical verdict of IPS). The public-api.json entries were edited explicitly for these four methods, not regenerated from the changed assembly. Original source-manifest provenance and wire fixtures remain unchanged. SendPain002Async keeps 202 until it is built.
