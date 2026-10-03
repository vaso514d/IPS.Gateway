# ADR 0002: Preserve the external contract while rebuilding internals

Status: Accepted for the foundation.

## Context

The owner wants a clean rebuild that existing core banking consumers can adopt without changing their HTTP contract or .NET contract references.

## Decision

Import Contracts unchanged from source commit `d498de6c4638aa71cdb20189d13642b41abab5f1`. Preserve package/assembly identity, version, namespaces, public members, route metadata, JSON shapes, status meanings, and callback contracts. Keep the original net8.0 target and dependency-free package.

Use `IPS.Middleware` for new internal projects. Translate Contracts at Api and Infrastructure edges; Application references Domain only. Use frozen public-interface and serialization baselines against the referenced Contracts assembly to detect accidental changes. The source manifest records initial import provenance; compatible source edits do not require byte-for-byte identity.

## Consequences

The existing spelling and mixed JSON property casing remain part of compatibility. Preserving declarations does not copy the old controller convention, error filter, or workflow implementation. Each capability must prove HTTP behavior separately when its endpoint is added.

A deliberate external change requires an approved compatibility/versioning decision and new independent expectations. Never refresh a baseline from the changed implementation merely to make a failing test pass. Internal database design is free to change; existing data migration is outside this milestone.

## Approved synchronous outbound exception (2026-10-04)

The owner approved replacing initial outgoing 202 with final 200 (including business rejection) or unresolved 504 with TransactionStatusDto after 30 seconds of durable intake. Duplicate submissions immediately return 200 current status, including Processing, and never start another attempt. Methods, routes, JSON shapes, reference semantics, callbacks and status-query acknowledgement remain preserved. Implement route metadata and independent baseline changes explicitly in Stage 2c; the aggregate refactor changes no Contracts source or baseline. See [the approved plan](../rebuild-plan.md).
