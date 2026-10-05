# 005c: Outgoing workflows and protocol

Base: 54d1d0b on codex/readability-refactor. Implements increment 3 of 005; no merge authorized.

Investigation now has explicit preparation, dispatch, evidence interpretation, scheduling and completion methods. Private execution state remembers only successfully committed outcomes; an immutable context groups the accepted payment, original references and fixed deadline. The former nested local workflow functions are removed. pacs.008 records its transitions directly before the shared release/commit boundary instead of passing mutation callbacks. Callback delivery separates abandoned-owner recovery from a single bounded HTTP send. pacs.028 construction names its header, group and transaction elements; trusted reply interpretation has its own phase after signature validation.

Reviewed and retained the already explicit intake, validation, recovery, status acknowledgement and pacs.008 mapping/signing helpers. No validation or cryptographic guard was removed, and no retry, cancellation, correlation, XML ordering, schema, Contracts or configuration behavior changed.

Independent Standards: clear; named steps improve checkpoint visibility without a generic orchestration framework. Independent Spec: clear; committed results, saved-response priority, deadlines, cancellation and wire construction remain equivalent.

Verification: 876 tests pass (257 unit/architecture/Contracts and 619 integration), no failures or skips. Release build, format verification, EF model consistency and whitespace checks pass. Full LocalDB, crash/recovery, Java-signed protocol and host tests are included. Results: readability-outgoing.trx in both test projects.
