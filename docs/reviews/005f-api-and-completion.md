# 005f: API controllers and readability completion

Original increment base: 3eaf337 on codex/readability-refactor. Completes increment 6 of 005. No merge authorized.

Recommit (2026-10-06): after the branch was reset to 077470b, the owner requested another commit. All six increments are consolidated in the commit containing this note. The staged snapshot matched e9ee28e exactly before these checkpoint-only documentation edits. The table below records the original review checkpoints; implementation and test evidence are unchanged.

## Change

OutgoingPaymentsController replaces the minimal outgoing handlers. It keeps the existing route constants, operation names, HTTP Results, request mapping, status acknowledgement and final/duplicate 200 versus unresolved 504 behavior. Disabled execution removes the controller from MVC discovery and OpenAPI. Query binding retains repeated values. A small input formatter calls the same HTTP JSON reader, keeping content types, encodings, serializer options and nesting behavior together.

Configuration factories now have named methods and still resolve after host configuration is finalized. Remaining single-line statements were expanded without flattening readable expressions. All 232 current authored production C# files are audited in the inventory; appropriate existing direct code is retained. README and architecture now describe the implemented, opt-in incoming and outgoing host instead of the older foundation-only state.

## Standards

Independent review and final recheck: clear. The controller remains focused on HTTP mapping and calls concrete Application handlers. Compatibility binding has a demonstrated purpose. Configuration extraction adds no framework or dependency changes. Existing queue/scope/registration abstractions have distinct responsibilities; no actionable findings remain.

## Spec

Independent review identified MVC's additional text/json support. The fix reuses the existing HTTP JSON reader rather than reproducing its charset/depth policy. Final recheck: clear. Routes, JSON, query comparison, application validation, acknowledgement, opt-in behavior and business workflows are preserved. Contracts are unchanged.

The 21 new HTTP regressions pass against both an isolated export of 3eaf337 (before controllers) and the final controller implementation. They cover malformed/missing/null bodies, unsupported media types, vendor JSON, UTF-8/UTF-16/Latin-1, quoted numbers, ignored nested properties, repeated queries, status serialization, validation problems, method constraints and OpenAPI metadata.

Framework references used for the compatibility check: [controller behavior](https://learn.microsoft.com/en-us/aspnet/core/web-api/?view=aspnetcore-10.0), [minimal request binding](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Http/Http.Extensions/src/RequestDelegateFactory.cs), and [MVC JSON input formatting](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Mvc/Mvc.Core/src/Formatters/SystemTextJsonInputFormatter.cs).

## Verification

The final working-tree run passes **897 tests**: 257 unit/architecture/Contracts and 640 integration; zero failures/skips. Release build has zero warnings/errors. Formatting, generated EF model consistency and diff whitespace checks pass. LocalDB, independent signed XML/HTTP fixtures, restart/concurrency tests and host smoke tests are included. Results: readability-final.trx in both test projects.

Fresh source export: restore and Release build pass with no existing build outputs; all **897 tests pass** again (257 + 640, zero failures/skips). Production and test sources match the working tree byte-for-byte. Results: readability-fresh.trx in both exported test projects.

## Completed rewrite

| Increment | Original review checkpoint | Review |
|---|---|---|
| Models and Domain | 95ca779 | 005a |
| Typed persistence, repositories and unit of work | 54d1d0b | 005b |
| Outgoing workflows and protocol | 51986d0 | 005c |
| Incoming workflows and protocol | 23860b1 | 005d |
| Clients, certificates and supervised runtime | 3eaf337 | 005e |
| Controllers and completion audit | e9ee28e | 005f |

The only schema change is the EF-generated TypedPaymentMetadata migration recorded in 005b; it changes discovery indexes and preserves historical migrations. Rebuild databases remain disposable/fresh-schema only. No authored production record declarations or EF.Property queries remain. Original/reference repositories were read-only throughout; existing branches are preserved.

Next: owner review and merge decision for investigation 077470b plus the consolidated cleanup commit. After approval, resume the separately specified protocol-authorized resend/runtime investigation slice, followed by unsolicited incoming pacs.002 handling. No new capability or production activation was added by this rewrite.
