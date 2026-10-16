# Stage 1 revision: shared unit of work and repository layout

Owner approved this revision after reviewing the aggregate implementation. Continue on codex/aggregate-events in the copy repository. Keep all existing uncommitted work; no merge or history rewrite is authorized. At revision start the live branch points to e24c28d, with the previous Stage 1 implementation present as working-tree changes. bc17a81 is a historical comparison object, not the live branch tip.

## Intent and evidence

The previous SqlTransactionUnitOfWork mixed transaction management with payment claims, immutable request checks, and duplicate client-reference classification. The owner requested one unit of work for all tracked changes and separate repository folders. The local example inspected was D:\WorkTornike\newGitlab\rsi-daily-reports\DailyReports\src\DailyReports.Infrastructure\UnitOfWork\UnitOfWork.cs and its Abstractions/IUnitOfWork.cs.

Adopt a small SaveAsync interface and collection of events through AggregateRoot. Retain commit-before-acknowledgement and explicit event names/versions from the middleware design. Do not introduce the example's event-sourced stream replay, publication flags before save, service locator, telemetry dependencies, or ExecuteInTransactionAsync.

## Acceptance

1. Application/Abstractions/Persistence/IUnitOfWork exposes Task<int> SaveAsync(CancellationToken = default). The return value is the total entries written across entity/event saves, or zero for no changes.
2. Infrastructure/UnitOfWork/UnitOfWork saves all tracked inserts, updates and deletes, including ordinary entities without domain events. Payment deletion remains specifically prohibited by payment persistence policy. The unit of work has no OutgoingPayment, claim, client-reference, or request-JSON decisions.
3. All changes use one scoped EF context and one local transaction. Write entities first so rowversion checks precede event inserts. A failure in either phase rolls back all entity/event changes. Acknowledge each aggregate's pending event snapshot only after commit. Failed units cannot be reused.
4. DomainEventsInterceptor uses AggregateRoot for event collection and sequence validation; it protects append-only history and requires the explicit save boundary. PaymentPersistenceInterceptor owns payment deletion, immutable request, and ownership validation. It changes no business state.
5. Expose general PersistenceConcurrencyException and UniqueConstraintException through Application abstractions, preserving underlying exceptions. The intake workflow alone interprets a unique failure as a duplicate reference by reading the persisted winner. Without a winner, rethrow the uniqueness failure. Work workflows translate concurrency failures into their existing no-claim/Conflict outcomes.
6. Interfaces have individual files under Application/Repositories/Payments; implementations live under Infrastructure/Repositories/Payments. Payment result/claim/history models stay in Application/Transactions. Mapping, event persistence and interceptors live under Infrastructure/Persistence.
7. Preserve SQL uniqueness, rowversion fencing, expired ownership recovery, scheduling, normalization, domain transition rules, Contracts, schema and historical migrations. Keep the existing TransactionDbContext CLR identity and migration paths so historical EF migration discovery remains intact; it is the shared context and is physically under Persistence.
8. No new production entity or feature is introduced for this revision. Verify support for ordinary entities using test-only mapped records/tables in isolated SQL databases. Host registration remains deferred to payment endpoints.

## Verification

Retain existing domain/application/SQL/host/compatibility tests, adapting assertions to SaveAsync and general persistence exceptions. Add meaningful SQL tests for ordinary insert/update/delete, mixed ordinary/aggregate atomic commits and rollback, concurrency rollback of other tracked changes, generic uniqueness errors, and shared scoped registration. Add an intake test proving unrelated unique failures propagate without a reference winner. Verify build, formatting, EF model consistency, and independent Standards/Spec reviews. Update the resume checkpoint and present the changes for owner review. After verification, the owner explicitly authorized committing this revision; merge approval remains separate.
