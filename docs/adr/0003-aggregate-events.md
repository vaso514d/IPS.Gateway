# ADR 0003: current-state aggregates with accompanying domain events

Status: Owner-approved design; implementation awaits merge approval.

OutgoingPayment owns business transitions through private Stateless 5.20.1 configuration. AggregateRoot owns identity and pending immutable events. Incoming payments get a separate model when implemented. Domain contains no EF or scheduling metadata.

EF maps current aggregate state directly. Infrastructure shadow properties hold request JSON, rowversion, ownership and scheduling. Application explicitly loads, invokes business methods, stages metadata and commits through repositories sharing one scoped context.

A unit of work saves the version-checked parent before append-only versioned TransactionEvents, inside one transaction. A small interceptor prepares JSON event rows; it dispatches nothing. Commit acknowledges pending events. Failed scopes are discarded. History is an audit record; no event replay establishes current state.

Repeated or conflicting final replies append observations retaining the original outcome. Only explicit operator resolution changes a final outcome. Technical observations advance event sequence without replacing current outcome details.

The generated schema replaces legacy TransactionHistory; the complete historical migration chain is for fresh databases only. Recreate disposable databases rather than converting history. No populated-database or production migration is supported.

The next capability will process outbound attempts synchronously with durable checkpoints and service-owned scopes. The approved HTTP 200/504 exception is documented in rebuild-plan.md and implemented only in Stage 2c.

## Shared save revision

The owner requested a general IUnitOfWork.SaveAsync over all tracked changes. The unit collects pending events through AggregateRoot; payment storage validation belongs in a dedicated interceptor and duplicate-reference interpretation belongs in Application intake. Repositories have dedicated Application/Infrastructure folders. Save returns the written-entry count and reports general persistence exceptions. Preserve the existing database schema and historical context CLR identity. See [the revision specification](../specs/001e-shared-unit-of-work.md).
