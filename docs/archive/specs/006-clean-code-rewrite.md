# 006: Clean-code rewrite (supersedes 005)

Owner-approved plan, 2026-10-06. Branch `codex/readability-refactor`, on top of the unmerged 005 commit `becb5ee`. One commit per slice, owner review after each. The owner approved the merge on 2026-10-06; codex/capability-rebuild was fast-forwarded to the merge checkpoint. No business process, external behavior, Contracts, JSON shape, event name/version or database schema changes.

## Why

The owner judged the 005 pass a flawed first draft. It had grown `src` by about 5,800 lines, mostly by:

- replacing records with classes that hand-wrote equality, copy constructors and operators;
- adding a defensive persistence layer that re-checked writes the domain, repositories and SQL already enforce;
- leaving dense workflows and repositories.

006 re-reviews the code for SOLID principles, clean code and clutter, not only formatting.

## Decisions

- **Records and classes.** Records are used for domain events, value objects, request/result models and protocol snapshots. Classes are used for aggregates, EF rows, services, validated values with factories (`ValidatedPacs008`, `InboundReceipt`), and options validated in their constructors.
- **One check per rule.** Each rule is checked once, at its owning boundary:
  - Application validates external input.
  - The Domain guards state.
  - Repositories own claim ownership, write-once evidence and commit-before-next ordering.
  - SQL constraints fence races.

  Argument re-checks on values our own code produces, and re-validation layers, are removed.
- **Shared behavior** is extracted only after two features demonstrate it. Examples: `ClaimedPayment`, `ClaimedIncomingPayment`, `PaymentOwnership`, `SupervisedBackgroundService`.
- **Unchanged decisions.** Options stay immutable and validated in their constructors; they are resolved once at startup (`ValidateMiddleware`) instead of being converted to mutable `IOptions<T>`. Infrastructure feature registrations stay public because tests compose them individually; the Api has one `AddMiddleware` entry point.

## Reference examples

DR is `rsi-daily-reports/DailyReports/src` and LS is `ListingSearchDataSync/src`. Both are read-only.

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

## Slices

| # | Commit | Change | Tests |
|---|---|---|---|
| 1 | 591e881 | Restored records (about 90 types); events carry only their data and `Raise` stamps identity | 897 pass; 116 mechanical test lines changed |
| 2 | ee761be | Removed 6 save interceptors, `PersistenceChanges`, `StagedChanges` and `RequireUsable`; explicit `UnitOfWork` keeps the parent-before-evidence fence; tidied EF configurations | 33 guard-only cases removed; 864 pass |
| 3 | dfe89c1 | Repositories rewritten around `PaymentOwnership`; argument re-checks and duplicated investigation rules removed | 4 argument-check assertions removed |
| 4 | cbe9bf1 | `ClaimedPayment` shared by the pacs.008 and investigation workflows; explicit cancellation tokens | No test changes |
| 5 | 8e18524 | `ClaimedIncomingPayment`, `IncomingProcessingResult.Of`, `PaymentMessageTypes.IsPacs008`, `IncomingReplyContext.New` | No test changes |
| 6 | 583fd89 | `SupervisedBackgroundService` shared by incoming workers and `OutgoingRuntime`; discovery query moved into the repository; `IpsHeaders`; certificate and reply checks named | No test changes |
| 7 | 7347752 | Api `AddMiddleware`/`ValidateMiddleware`, shared configuration reading, consistent registration names; 005 documents replaced by this spec | No test changes |

The 005 migration `20261005181308_TypedPaymentMetadata` (discovery indexes for typed metadata) remains. 006 adds no migration.

## Verification

Every slice ran a clean Release build, the full test suite (LocalDB and process tests), the `dotnet format` check, an unused-usings check and `dotnet ef migrations has-pending-model-changes`.

The final state passes 864 tests: 257 unit/architecture/Contracts and 607 integration. The baseline of 897 minus 33 guard-only cases gives 864.

Since `becb5ee`, `src` changes by +3,640/−5,417 lines. Authored production code is about 12,000 lines, down from 13,750.

One intermittent observation: `OutgoingCrashTests.Killed_process_resumes_only_from_committed_evidence("response")` failed once in a full run and then passed in isolated and full reruns. No cause was established; it resembles the process-test observation recorded in `003a1`.

## Open findings

These were not changed and are left for the owner to decide:

- `OutgoingTransactionWork.TryStartAsync`, `InboundWorkDiscovery` and `IncomingPaymentRegistration` have no production callers; only tests use them.
- `InvestigationExecution` remained a seam for the pending investigation runtime; 003a.3 removed it when OutgoingRuntime took over investigations.
