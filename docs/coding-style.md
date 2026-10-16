# Coding style

Apply to authored Domain, Application, Infrastructure and Api code. Contracts and generated migration files retain their existing representation.

- Use records for domain events, value objects, request/result models and protocol snapshots; never hand-write equality, copy constructors or operators for them. Use classes for aggregates, EF rows, services, validated values created through factories, and options validated in their constructors. Entities change only through named operations.
- Write one operation per line and use braces for control flow. Prefer descriptive names, including full dependency names (`unitOfWork`, `timeProvider`, `cancellationToken`). Use expression bodies for simple accessors or delegations, not multi-step workflows. Break LINQ chains one operator per line.
- Keep feature workflows as directly called concrete handlers. Read the public method top to bottom as named steps; replace compound conditions with named predicates. Avoid large closures, capturing local functions and mutable state captured across commits. Pass cancellation tokens explicitly.
- Put repository/client interfaces at the Application seam and implementations in Infrastructure. Use typed LINQ queries and fluent entity configurations; EF-only mapping exceptions stay in configuration. Queries belong in repositories, not in workers or hosts.
- Check each rule once, at its owning boundary. Application validates external input; the Domain guards state; repositories own claim ownership, write-once evidence and commit-before-next ordering; SQL constraints fence races. Do not re-check values produced by our own code, and do not add re-validation layers such as save interceptors or authorization bookkeeping. Remove a check only when its enforcing boundary and regression evidence are identified.
- Keep transaction, claim and checkpoint boundaries visible. Preserve atomicity, cancellation semantics and immutable evidence while simplifying implementation.
- Add an abstraction for an external dependency, or for behavior that two implemented features already share. Avoid generic workflow/repository frameworks, service location inside workflows and pass-through wrappers.
- Name protocol constants (message definitions, header names, OIDs) once. Prefer a short explanation of a non-obvious invariant over comments that repeat the code; use plain `//` comments, not XML documentation tags. Readability has no arbitrary line-count target.
- Options stay immutable and validated in their constructors, resolved once at startup by `ValidateMiddleware` rather than converted to mutable `IOptions<T>`. Infrastructure feature registrations stay public so tests can compose them individually; the Api has one `AddMiddleware` entry point.
- Existing shared behavior extracted after two features needed it: `ClaimedPayment`, `ClaimedIncomingPayment`, `PaymentOwnership`, `SupervisedBackgroundService`. Reuse these before adding another.

## Reference examples

DR is `rsi-daily-reports/DailyReports/src` and LS is `ListingSearchDataSync/src`. Both are read-only, outside this repository.

| Code | Reference | Rule |
|---|---|---|
| Aggregates | DR `Domain/Aggregates/CertificateAggregate/Certificate.cs` | Private setters, factory, named operations, `Raise(new Event(...))` |
| Events, values | DR `.../Events/GamePackageCreated.cs`, LS `Domain/ValueTypes/ExternalId.cs` | `sealed record`, no hand-written equality |
| Input/result models | DR `AddCertificateCommand.cs`, LS `DTOs/UpsertEmbeddingRequest.cs` | Records; `with` instead of copy constructors |
| Workflows | LS `Handlers/JobFacetsUpdatedHandler.cs` | Named steps, early returns, intent-named methods |
| Repositories | DR `Repositories/CertificateRepository.cs` | Typed LINQ, one operator per line, no business rules |
| Unit of work | DR `UnitOfWork/UnitOfWork.cs` | Explicit save steps, no interceptors |
| EF configuration | DR `Persistence/Configurations/CertificateConfiguration.cs` | `builder`/`x`, relationship chains one call per line |
| DI | LS `Infrastructure/DependencyInjection.cs` | One composed entry point of grouped `AddX` methods |
