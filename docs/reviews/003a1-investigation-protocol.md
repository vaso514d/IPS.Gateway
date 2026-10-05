# Review 003a.1: investigation protocol

Branch codex/outgoing-investigation, base b2542e1. Owner approved and completed the outgoing-host fast-forward into codex/capability-rebuild. Owner approved committing this protocol increment on 2026-10-05. Merge approval remains pending.

## Delivered

Pacs028Xml builds schema-validated investigation messages from frozen payment values and supplied request identity, preserving original identifiers and submillisecond acceptance time. Signing reuses the certificate and explicit-development policy. Pacs028ReplyInterpreter separates a final original-payment report from an investigation rejection; only trusted, correlated AG09/RJCT/1016 evidence yields NotFound. This result performs no send and creates no authorization on its own.

The ordinary payment interpreter keeps its public compatibility behavior. Its shared internal investigation path additionally rejects mixed reason codes and contradictory nested original-message references. Message definitions are exact, signatures are mandatory on replies, and unsuccessful/malformed/ambiguous evidence remains unresolved.

Pinned schema: original d498de6, IPS.MiidleWear.Tests/Ips/Schemas/pacs.028.001.06.xsd; SHA256 146F5E48430E126B35FFEA8454AA86D56E0BCB092EB992B53BACD5B4E06808A5. Imported unchanged. No database, Contracts, host activation or remote dispatch changes.

## Standards

Independent review found misleading fallback wording describing an investigation rejection as payment rejection. Fixed with a message-specific diagnostic and regression. Recheck: no remaining actionable findings.

## Spec

Independent review found first-reason-only classification and ignored nested correlation. Both fixed only for investigation interpretation, with independently signed mixed-reason and nested-ID/version fixtures and matching-reference positive controls. Recheck: no remaining actionable findings.

## Verification

Final verification passed: **849 tests**, comprising 252 unit/architecture/Contracts and 597 integration tests, zero failures/skips. This includes 34 new protocol cases. Release build has zero warnings/errors; formatting verification, EF model consistency and whitespace checks pass. The pinned schema was compared byte-for-byte against the source Git object. Real SQL, process recovery and host checks are included. Local reports: tests/IPS.Middleware.Tests/TestResults/investigation-protocol-verified.trx and tests/IPS.Middleware.IntegrationTests/TestResults/investigation-protocol-verified.trx.

During verification, an existing one-second HTTP response-body test repeatedly timed out before reaching its simulator under full-suite startup load; isolated runs passed. Instrumentation confirmed calls=0 and a request timeout before body readiness. Its test class now runs outside parallel SQL/process startup, keeping the one-second timeout and original assertions. The handshake reports early request failure directly. Production transport is unchanged; temporary debug tags were removed. The additional test-only change passed independent Standards review.

One diagnostic full-suite run also returned HTTP 500 in the existing lost-reply host test. Its original assertion did not retain the response body, so the cause could not be established. The assertion now captures that body on failure. It passed in the final complete run and three subsequent focused reruns (both normal uncertainty and lost reply each time). This is an unresolved intermittent test observation, not a claimed production fix.

## Remaining

Durable investigation requests/responses, retry counters/deadlines, one-use resend authorization and runtime scheduling follow in the next focused slice. Incoming unsolicited pacs.002 status application is also required for full source parity. Source scheduling/counting contradictions and real IPS correlation/retention remain in the roadmap. No production interoperability or complete outgoing reliability is claimed.
