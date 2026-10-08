# ADR 0001: One executable with layered libraries

Status: Accepted for the foundation.

## Context

The reference repository deploys outbound API and inbound Gateway independently. The owner wants simpler consumption and one running application while preserving a clean separation for workers.

## Decision

Use one Api executable. Domain and Application own payment behavior; Infrastructure provides adapters and hosted scheduling. Project references enforce the inward dependency direction. Worker scheduling remains separate from application workflows.

## Consequences

Local startup and deployment need one host. HTTP intake and workers share process resources and lifecycle; they cannot scale independently in this design. Durable work and concurrency controls remain necessary when several complete hosts run.

If independent deployment becomes a concrete requirement, add another composition host over the existing workflows and adapters. Do not introduce configurable host roles in the foundation.
