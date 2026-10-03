# Contract compatibility baseline

Source repository: https://github.com/vaso514d/IPS.MiidleWear

Source commit: `d498de6c4638aa71cdb20189d13642b41abab5f1`.

The source Contracts files were exported from their raw Git blobs and built independently of the new repository. Expectations were captured from that original net8.0 assembly.

- `source-manifest.json`: original Git blob identities of every imported source/project file. The test reconstructs Git's blob hash and checks the complete copied file set.
- `public-api.json`: exported types, constructors, members, constants, nullability, and attributes, including route metadata and enum values.
- `wire-cases.json`: representative JSON inputs with exact output captured from the original assembly using web serialization and null omission, matching the reference HTTP serializer configuration.

The 15 wire cases exercise nested models, optional fields, property casing, dates/times, identifiers, status enums, callbacks, and proxy operations. They characterize serialization; they are not payment-validation or protocol-compliance tests.

`ContractSnapshot.cs` computes the actual public surface in tests. There is intentionally no automatic baseline refresh step. A Contracts or baseline change requires an approved compatibility decision with independently established expectations.

The original project file preserves its NuGet package metadata. The root configuration keeps it packable without publishing a package.
