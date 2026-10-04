# 2a.2 review: validation, typed XML and signing

Branch codex/pacs008-protocol, base c1674e6c4c06532bab6b57fdb7fe6a3fba514633. Owner approved the merge on 2026-10-04. Feature commit eac12af243cab69543903870a9b2cf266e39aaa4 was fast-forwarded into codex/capability-rebuild. Specification: [002a2](../specs/002a2-pacs008-protocol.md). See the ledger for the resulting commit checkpoint.

## Diff guide

- Application/Payments/Pacs008/Validation: core FluentValidation 12.1.1 composed payment, party/address, and remittance/initiation validators; checksums remain small pure functions. Replaces the large Pacs008Rules method. Explicit invocation preserves error fields/messages and optional-child behavior.
- Application/Payments/Pacs008/ValidatedPacs008 and PaymentDetails: immutable normalized required values, typed party/account/priority concepts, policy resolution, and defensive collection snapshots. No nullable raw request is exposed downstream. Source split-before-trim address behavior remains explicit.
- Infrastructure/Payments/Pacs008/Pacs008Xml: Build now takes only the validated payment and message context. A constructor-supplied validated protocol profile owns settings. Concrete mapping, typed supported-profile XML models, XmlSerializer, and XSD validation have separate responsibilities. XML models remain in Infrastructure.
- Directory.Packages.props, Application project and architecture checks: narrowly permit pinned FluentValidation in Application; Contracts remains dependency-free; Domain remains limited to Stateless. No ASP.NET validation integration or generic mapping/mediator framework.
- Signing: focused ECDSA-SHA256 signer, explicit development-only unsigned policy, caller-owned certificates, SHA256 whole-message digest, and independently verified restricted SignedInfo canonicalization. Host configuration defaults to signing required and rejects unsigned permission outside Development.
- Tests: normalization, child errors, read-only snapshots, field mapping, profile codes, whitespace/chunking, culture-independent lexical output, configuration checks, parallel builder reuse, schema order/namespaces and DTD rejection.
- Architecture/spec/research/ledger record the model strategy and resume checkpoint. Generator output and comparison harness are ignored scratch artifacts.

Contracts, Domain, database model/migrations, routes, status meanings, and execution flow are unchanged. No HTTP payment operation, sender, worker or remote call is added. Certificate-source loading/rotation and durable workflow orchestration remain separate upcoming capabilities.

## Source and design evidence

Original behavior reference: d498de6c4638aa71cdb20189d13642b41abab5f1; source paths and exact XSD hashes are in the specification. Existing Treasury comment/runtime discrepancy is retained as documented. The prior postal-address review correction remains covered.

A dotnet-xscgen 3.0.1405 trial generated both pinned schemas successfully, producing 199,475 bytes of full-schema C#. Authored supported-profile XmlSerializer models were selected for the smaller supported surface and explicit seven-digit UTC/decimal lexical representation. No generator is required to restore/build. See [research](../research/pacs008-xml-validation-design.md).

A temporary differential harness matched the prior implementation for 269 request-validation variations and XML for 141 valid requests. This is preservation evidence, not correctness proof: independent source review additionally caught a pre-existing currency-normalization gap and the regression now verifies the corrected behavior against the pinned source.

## Verification

300 tests passed, zero failed/skipped: 200 unit/application/architecture/Contracts and 100 integration (including SQL, host, XML and signing tests). Release build zero warnings/errors. Formatting verification, git whitespace check and EF migration/model consistency pass. A clean source export excluding bin/obj, Git metadata and scratch tooling restored, built, and passed the same 300 tests. Host tests verify liveness and explicit development-only configuration behavior without exposing payment endpoints.

Java's independent JSR105 verifier accepts generated signatures with original, extra-prefix and default-header namespace contexts and rejects tampered header/amount/signature or a different public key. Certificate tests cover missing, expired, future, public-only, RSA and incompatible usage cases, plus signing-capable content-commitment and P-384 certificates. A real SQL test signs committed unsigned XML, saves the signed result, reloads it and verifies it independently. Tests use ephemeral certificates; Java17+ is test-only and provisioned explicitly in CI. Local verification used JDK20.0.2.

## Standards

Independent read-only review found one P3 readability issue in dense normalized-value construction. Resolved with private named normalization methods, local collection steps and named arguments. Re-review: no remaining actionable Standards findings. No documented architecture violations found. The subsequent independent signing Standards review also found no actionable issues.

## Spec

Independent read-only review found one P2 source-compatibility issue: currency retained a trailing newline accepted by the original regex, causing later schema failure. Pinned ContractToDomainMappings.Required and Pacs008PaymentInstruction trim currency. The validated model now trims it and a validation-through-XML regression covers GEL followed by a newline. Re-review: no remaining actionable Spec findings in validation/XML scope. The subsequent signing Spec review found no actionable issues in signature structure, canonicalization restrictions, certificate ownership, configuration gating, or independent verification.

Findings: Standards 1 resolved / 0 remaining; Spec 1 resolved / 0 remaining. Test execution was performed by the implementing agent, separate from the read-only reviews.

## Remaining capabilities and limits

The owner chose explicit development-only unsigned mode, and this policy is implemented. Signing uses supplied certificates; certificate-source loading/rotation will be wired when processing/host composition needs it. Stage2b owns durable orchestration and submission checkpoints; Stage2c owns transport, supervised HTTP waiting and final-status delivery.

SignedInfo canonicalization is restricted to the generated profile: xml:* attributes on its ancestors are rejected, inherited namespaces are materialized, and an independent C14N1.1 implementation verifies output. This is not a general C14N1.1 implementation. No real IPS endpoint or certificate trust chain has been exercised. Existing stored artifacts remain immutable and must be reused rather than re-signed.

Protocol sources: [W3C XML Signature1.1](https://www.w3.org/TR/xmldsig-core1/), [Canonical XML1.1](https://www.w3.org/TR/xml-c14n/), [RFC5280 key usage](https://www.rfc-editor.org/rfc/rfc5280#section-4.2.1.3). Pinned original signing source is Application/Security/IpsXmlSigner.cs at d498de6.
