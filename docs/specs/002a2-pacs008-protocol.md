# 2a.2: pacs.008 request validation and protocol preparation

Base c1674e6, merged into codex/capability-rebuild with owner authorization. Branch codex/pacs008-protocol. Original reference d498de6c4638aa71cdb20189d13642b41abab5f1.

## Source evidence and behavior

Read at the pinned reference:
- Application/Validation/Pacs008InstantPaymentRequestValidator.cs, ValidationPatterns.cs, TransactionRules.cs, TaxCodeValidator.cs.
- Application/Mapping/ContractToDomainMappings.cs, Domain/Payments/Pacs008PaymentInstruction.cs and its mapper.
- API/Services/Pacs008XmlMessageBuilder.cs and Pacs008IdentifierGenerator.cs.
- Application/Xml/IsoXmlWriter.cs.
- Tests/Api/Pacs008InstantPaymentTests.cs plus existing head.001.001.03 and pacs.008.001.12 schemas.
- Application/Security/IpsXmlSigner.cs, XmlDsigC14N11Transform.cs, and Tests/Security/XmlDsigC14N11InvariantTests.cs.
Project paths have the IPS.MiidleWear. prefix.

Application owns validation. Its input is independent of Contracts, and a privately constructed ValidatedPacs008 snapshots collection inputs so later caller edits cannot invalidate it. Errors have field/message structure. Api mapping and the HTTP error envelope arrive in 2c; no external DTO changes here.

Preserve required clientReference/instructionId/endToEndId (ASCII, max35), timestamps, positive amount (13 integer/5 fractional digits), uppercase enabled currency/configured limits, NORM/HIGH, category code (1-4 alphanumeric), required debtor/creditor type/name/account/creditor BIC, Georgian organisation tax checksum, IBAN checksum, indirect-payer membership, distinct accounts/banks and own debtor participant. Preserve original character repertoires and optional fields, address lengths, remittance lengths, MCC rules, channel/instrument codes and counts.

Ordinary acceptance is within 0..1 second after creation. RTP-/PSP- end-to-end IDs require a suffix and acceptance no later than creation. No new age/deadline policy here.

Infrastructure builds Message containing head.001.001.03 AppHdr and pacs.008.001.12 Document using existing stored identifiers. AppHdr/BizMsgIdr equals GrpHdr/MsgId; TxId is the persisted protocol transaction ID. Do not generate identifiers inside XML generation. ClientReference is never written to XML. Envelope creation is an explicit UTC instant; business times remain the request times, formatted UTC. Settlement date is acceptance converted to UTC+4. Fixed CLRG/IPS/local-INST/SLEV/GE/BILL values and XSD sequence follow source. Service-level code is configurable with default INST, and remittance location method is configurable with default URID, preserving source options. Address lines split70, unstructured/additional remittance split140 without truncation; optional empty elements are omitted. Category/reference types are uppercased as in the source mapping.

Validate the two envelope children with embedded original schemas. Disallow DTD/external resolution; validation must not depend on network/files outside the assembly. Test minimal/full/treasury/initiation cases, independent expected element values, namespaces, ordering and malformed XML.

## Gaps and decisions

- Treasury DTO comment says IBAN, while implementation, dedicated test and mapping use Id/Othr/Id with a configured Treasury receiver. Preserve the implemented, tested Treasury exception; do not edit imported Contracts comments.
- Empty currencies only bypassed source validation in bare unit setups; source host rejects empty configuration. New policy requires configured currencies.
- Source validators and subsequent mapping are not identical for null collection items. Invalid structured-reference/instrument items return field errors instead of failing later in mapping. These are internal preparation safeguards; no HTTP error shape is established here.
- Owner approved explicit development-only unsigned mode on 2026-10-04. Default is signing required. Payments:Signing:AllowUnsignedInDevelopment must be explicitly true and the host environment must be Development. Reject that option in every other environment. A supplied unusable certificate must fail rather than silently fall back.
- The source labels a C14N1.0 wrapper as C14N1.1 under constrained XML assumptions. Signing implementation must establish canonicalization correctness independently before claiming interoperability.

## Boundaries and verification

No remote calls, sender, submission markers, callbacks or payment endpoints. Protocol building does not commit or drive Domain transitions; the durable orchestration is Stage2b. Artifact storage from2a1 remains authoritative for subsequent retries.

Use meaningful validation cases, copied schema hashes/provenance, independent XML expectations and signature verification. Build/test/format/Contracts/architecture checks plus independent Standards/Spec review precede owner merge approval. This review is not complete while signing policy/implementation is unresolved.

Schema provenance (exact Git blobs from the original test schema directory):
- head.001.001.03.xsd: f0aaa5df4659bc03a8686f27717130e4b1265058
- pacs.008.001.12.xsd: 7e85f67eb8b7181f79aa0c849932561b3ba64554

Signing research reference: https://www.w3.org/TR/xml-c14n/ (Canonical XML 1.1) and https://www.w3.org/TR/xmldsig-core/ (XML Signature 1.1). This research is not proof that the original C14N wrapper is a general conforming implementation.
## Owner-approved validation/XML refactor — 2026-10-04

The owner approved replacing the dense validation/XML methods after reviewing the design research. Scope of this increment is validation and unsigned XML structure, not signing policy, workflow, or HTTP delivery.

- Pin core FluentValidation 12.1.1 in Application and update the dependency allowlist narrowly. Invoke validators explicitly. Compose payment, party/address, and remittance/initiation rules; preserve error fields/messages, null-child handling, limits, and policy rules.
- Successful validation produces an immutable normalized payment with required values, typed party/priority/account concepts, and defensive collection snapshots. Preserve source normalization, including raw address splitting before per-line trimming.
- Replace Build's configuration arguments and storage snapshot with a validated protocol profile and message context containing only persisted identifiers and explicit envelope time.
- Use typed Infrastructure XML models, mapping, XmlSerializer serialization, and the existing XSD check. Keep closed protocol choices typed and distinguish fixed profile constants from configurable codes.
- Evaluate generation from the pinned XSDs before choosing the model strategy. The successful generator trial produced approximately 200 KB of full-schema code. Authored supported-profile models were selected to limit unused surface and explicitly preserve seven-digit UTC timestamps and trimmed decimal lexemes. No tool is needed to build the repository.
- Preserve Contracts, namespaces/order/codes/optional omission, and all existing request behavior. No merge until owner review. Signing remains the next incomplete part of Stage2a.2.

## Signing increment — 2026-10-04

Continue on the current unmerged review branch. Implement a focused signer and host-bound signing policy, not certificate-source loading, a sender, or durable workflow orchestration (Stage2b).

- Sign stored unsigned XML with an explicitly supplied, caller-owned ECDSA certificate. Return XML plus an explicit signed/unsigned result. Without a certificate, only the approved explicit development option permits unchanged unsigned XML. A supplied invalid/expired/not-yet-valid certificate, missing private key, non-EC key or incompatible key usage fails (when present, digitalSignature or contentCommitment/nonRepudiation permits signing per RFC5280 section4.2.1.3); do not silently bypass signing.
- Preserve source signature profile: AppHdr/Sgntr/ds:Signature Id=MONT; empty-URI whole-message reference; enveloped-signature then inclusive C14N1.0 transforms; SHA256 digest; ECDSA-SHA256 signature in IEEE P1363 format; SignedInfo inclusive C14N1.1; X509SubjectName and X509Certificate.
- Parse without DTD/external resolution. Validate the schema before and after signing. Reject existing signatures/signature envelopes: retries use stored artifacts and must not silently re-sign. The signer does not own certificate loading/disposal, persistence or network access.
- Do not register process-wide custom cryptographic algorithms. Use .NET canonicalization and ECDSA primitives. C14N1.0 may implement the generated SignedInfo's restricted C14N1.1 subset only with an enforced rejection of xml:* attributes on SignedInfo/ancestors, explicit inherited namespace handling and independent C14N1.1 verification. This is not a general C14N1.1 implementation.
- Tests use ephemeral self-signed certificates and a separate Java JSR105 XML signature verifier. Cover valid signatures, key/profile metadata, header/document tampering, missing/wrong/expired/not-yet-valid/public-only certificates, existing signatures, unsafe XML, and development policy. No real certificates, network, or external payment services.
- Register policy from host configuration with unsigned permission default false. No payment endpoint or transport is introduced. Java17+ is a test-only prerequisite, explicitly provisioned in Windows CI.

Source evidence: pinned Application/Security/IpsXmlSigner.cs and XmlDsigC14N11Transform.cs. Standards: W3C XML Signature1.1 section6.4.3 (ECDSA P1363), Canonical XML1.1; Java XMLSignatureFactory/CanonicalizationMethod independently verify the exact advertised algorithms. Wire interoperability with the real IPS remains unverified until a later external environment exercise.
