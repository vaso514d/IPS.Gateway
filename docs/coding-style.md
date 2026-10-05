# Coding style

Apply to authored Domain, Application, Infrastructure and Api code. Contracts and generated migration files retain their existing representation.

- Use classes with explicit properties. Entities mutate through named operations; immutable values use constructor initialization or init-only properties and defensive collection copies. Implement value comparison only where it has a defined purpose.
- Write one operation per line and use braces for control flow. Prefer descriptive parameter/local names. Use expression bodies for simple accessors or delegations, not multi-step workflows.
- Keep feature workflows as directly called concrete handlers. Extract meaningful steps into named methods; avoid large nested closures and mutable state captured across commits.
- Put repository/client interfaces at the Application seam and implementation in Infrastructure. Use typed LINQ queries and fluent entity configurations. EF-only mapping exceptions stay in configuration.
- Validate external input once in Application. Domain owns state invariants, Infrastructure owns persistence/protocol checks. Remove a check only when its enforcing boundary and regression evidence are identified.
- Keep transaction/claim/checkpoint boundaries visible. Preserve atomicity, cancellation semantics and immutable evidence while simplifying implementation.
- Add abstractions for demonstrated shared behavior or an external dependency. Avoid generic workflow/repository frameworks, service location inside workflows and pass-through wrappers.
- Prefer a short explanation for a non-obvious invariant over comments that repeat the code. Readability has no arbitrary line-count target.
