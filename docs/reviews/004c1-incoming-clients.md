# Review 004c.1 — incoming HTTP clients and configuration

Branch: codex/incoming-clients. Base: af9b090, fast-forwarded into codex/capability-rebuild with owner approval on 2026-10-05. The prior slice passed 637 tests before that merge. Owner approved committing and merging the reviewed cleanup on 2026-10-05. This commit is the approved Review 1 checkpoint; other branches and the original repository remain preserved.

## Delivered

Infrastructure IPS receive/reply and CBS submission/status/reversal clients preserve existing wire contracts. Application receives immutable raw receive evidence. Microsoft.Extensions.Http.Resilience 10.10.0 supplies circuit breakers/timeouts only; response buffering sits inside the pipeline. No redirects, retries, hedging or fallback business responses. Settings cover URLs, paths, connection pools and timeouts, breaker behavior and separate certificate roles. Startup loads PFX/PEM/encrypted PEM/Windows-store sources, validates keys and configuration, and retains TLS hostname/chain checks. Windows file TLS identities use temporary named keys required by Schannel.

Api binds settings after final configuration and eagerly validates enabled clients without making remote or SQL calls. Transport defaults disabled. No workers, payment endpoints, workflow composition, schema/migrations or Contracts changes. [Specification](../specs/004c-live-incoming.md); [configuration](../configuration.md).

Source evidence: original d498de6, IPS.MiidleWear.Application/Transport/IpsHttpStpClient.cs and IpsHttpHeaders.cs; Gateway/Services/CoreSystemClient.cs, CoreSystemRequest.cs, CoreReferences.cs; Application/Security/CertificateSource.cs and IpsTls.cs. Existing contract constants and existing mappings are reused.

## Verification

Final Release build: zero warnings/errors. Full suite: **674 passed, zero failed/skipped** (249 unit/architecture/Contracts; 425 integration, including real SQL Server LocalDB and Java JSR105 fixtures). JAVA_HOME used JDK20.0.2. New transport suite contributes 37 cases with independent HTTP/TLS peers, including exact paths/headers/body, raw failed responses, malformed sequence evidence, UTF-8, timeout during body transfer, caller cancellation, breaker opening, absent hidden sends/redirects, mutual TLS, hostname/chain rejection, PFX/PEM/encrypted PEM, startup rejection and disabled/no-worker behavior.

Formatting verification, diff whitespace and official EF pending-model check pass. Model check reports no changes since the last migration. Existing host liveness/OpenAPI tests and the new configured host startup test pass without external dependencies. No production IPS/CBS connection occurred.

## Standards

Independent Standards review and recheck: **zero remaining findings**. The initial stale configuration statements were corrected, and Transport was added to the settings table. No hard architecture or code-standard violation and no additional smell-based refactoring was identified.

## Spec

Independent Spec review and recheck: **zero remaining findings**. The initial startup gap for incompatible IPS signature trust keys was corrected: every configured signature certificate must have an ECDSA public key and permit digital signatures. Added an RSA rejection regression test. No scope creep found.

## Owner-requested cleanup — 2026-10-05

Behavior-preserving; no settings, wire, schema or Contracts change:

- IncomingIpsClient and IncomingCbsClient share one internal HttpEvidence helper (single attempt, complete body, all response/content/trailing header values). Each client has one private send path that checks the participant and builds the request; IPS reuses the existing ReqSts header constant.
- IncomingHttpRegistration resolves the endpoint through one local function and names the IPS receive/reply connection split.
- CertificateSettings computes store/PFX source once instead of repeating extension checks; client-auth EKU validation moved beside RequireDigitalSignature.
- The participant check now runs after the request body is mapped, not before. A disabled or mismatched participant still throws before any HTTP send.

Verification after cleanup: zero-warning Release build; 249 unit and all 37 transport simulator tests pass, plus 65 host/settings/reply-interpreter tests; formatting and whitespace pass. LocalDB failed to start on this machine during the cleanup ("SQL Server process failed to start"), so the SQL-backed integration tests could not be rerun; none of the changed files touch persistence.

## Limits and next gate

Successful Windows certificate-store selection and service-identity private-key ACLs remain an operator-environment check; automated tests inspect a missing thumbprint read-only and do not install certificates in machine stores. Real IPS certificate chains and interoperability are not claimed. IPS late-reply acceptance/retention remains the existing pre-production verification question.

Owner authorized committing/merging this review into codex/capability-rebuild on 2026-10-05. Review 2 next composes receipt registration, CBS processing and reply delivery in fresh scopes. Review 3 adds polling/dispatch/reply/follow-up workers, channel recovery, admission limits, multi-instance and termination tests. Those reviews are not implemented here.

## Final cleanup review and merge authorization

Owner requested review and commit/merge if the cleanup was sound. Independent Standards and Spec re-reviews report zero actionable findings. Current Release build passes with zero warnings/errors; 249 unit/architecture/Contracts and 70 transport/host/settings tests pass. Formatting, whitespace and EF model consistency pass. Increased only the TLS simulator connection budget from 200 ms to 2 seconds after a scheduling-sensitive handshake timeout; production settings remain unchanged.

The full-suite rerun was attempted but blocked by LocalDB startup failure (Windows API error 575 / SQL runtime 0x89c5010a), despite restarting the identified stalled instance processes and trying its matching v15 utility. The failed SQL run was stopped; no databases were deleted during recovery. The earlier 674-pass result above predates the cleanup and is not presented as a current full-suite result. No persistence code, schema, Contracts or SQL tests changed. Merge proceeds on the clean scoped verification and independent reviews, with this environment limitation explicitly retained. Rerun the full SQL suite before the next capability review.
