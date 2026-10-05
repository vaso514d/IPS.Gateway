# 005e: Clients, configuration and runtime

Base: 23860b1 on codex/readability-refactor. Implements increment 5 of 005; no merge authorized.

Each incoming/outgoing HTTP adapter has its own file. HTTP setup resolves a profile once per handler/pipeline construction. Certificate loading separates store, PFX and PEM sources and Windows TLS adaptation; server trust validation is a named step. Configuration types retain their keys, defaults and validation. Receive workers expose receipt mapping and persist-before-next-poll recovery; discovery, follow-up dispatch and supervised callbacks have named methods. Shutdown and capacity checks remain at their existing boundaries.

Reviewed and retained the existing scoped registration, bounded queues, recovery queries, dependency registration and settings where they already have direct responsibilities. No retries, hedging, TLS bypasses, configuration changes or activation were introduced.

Independent Standards review: clear. Independent Spec review: clear. Final verification: 876 tests pass (257 unit/architecture/Contracts, 619 integration), no failures/skips. Release build, formatting, EF model consistency and whitespace checks pass. Includes real SQL, process recovery, HTTP simulators, certificates, cancellation and host tests. Results: readability-runtime.trx in both test projects.
