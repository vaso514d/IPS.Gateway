# pacs.008 XML and validation design research

2026-10-04 research. The owner subsequently approved the proposed refactor; implementation outcome is recorded below.

## Current problems

`Pacs008Xml.Build` mixes configuration checks, protocol defaults, business choices, normalization, tree construction, serialization, and XSD validation. `Pacs008Rules.Validate` mixes nested fields, policy rules, string paths, and algorithms. `ValidatedPacs008` wraps nullable request fields, leaving downstream null-forgiving operators and repeated trimming.

Wire names such as `GrpHdr` must exist somewhere. Closed protocol codes, configurable values, and business concepts such as `type == 0` deserve distinct treatment rather than one large constants class.

## XML approaches

Microsoft's `XmlSerializer` supports typed models with controlled XML names, attributes, and namespaces and is available in .NET 10. Generated schema models can eliminate handwritten structural names from mapping. Serialization does not establish our payment-policy correctness. [XmlSerializer](https://learn.microsoft.com/en-us/dotnet/api/system.xml.serialization.xmlserializer?view=net-10.0)

Microsoft's official `Xsd.exe` generates classes from XSD, but its documented location is Windows SDK/NETFX Tools; it is not a modern dotnet SDK command. [Xsd.exe](https://learn.microsoft.com/en-us/dotnet/standard/serialization/xml-schema-definition-tool-xsd-exe)

`XmlSchemaClassGenerator`/`dotnet-xscgen` is a third-party open-source alternative generating XmlSerializer-compatible models. Its maintainers list unsupported restriction/choice patterns, so evaluate it against our exact schemas before adopting. Generated code adds volume and schema-oriented types; keep it separate from handwritten mapping and inside Infrastructure. [Maintainer documentation](https://github.com/mganss/XmlSchemaClassGenerator)

The tool package lists a net8.0 asset and computed net10.0 compatibility. This does not prove CLI execution on a machine with only a .NET 10 runtime. No tool was installed or executed for this research. Pin and test generator/runtime requirements before adoption. [Published package](https://www.nuget.org/packages/dotnet-xscgen)

LINQ to XML is also an established Microsoft construction approach. Nested expressions resemble the output tree; they are not inherently poor design. Named mapping methods can improve the current code without generation, but element names and ordering remain manually maintained. [Functional construction](https://learn.microsoft.com/en-us/dotnet/standard/linq/functional-construction)

Keep explicit XSD validation with either approach. Microsoft provides XmlSchemaSet and validating readers/tree extensions; successful serialization is not a substitute. [Schema validation](https://learn.microsoft.com/en-us/dotnet/standard/data/xml/xml-schema-xsd-validation-with-xmlschemaset)

## Recommended boundary

Evaluate generation for only the pinned header and pacs.008 schemas plus a small explicit IPS envelope. Record schema provenance and reproducible generation commands. Reject the approach if the generated model obscures mapping more than it helps; composed LINQ mappings remain a viable fallback.

Have a feature builder orchestrate named header/group/transaction/party/remittance mapping, serialization, and XSD checks. Supply identifiers and envelope time through a typed context. Inject validated protocol settings for BIC/service level/remittance method instead of six positional parameters. Use generated enums for closed schema codes, validated strings for extensible code sets, and local named constants for fixed profile values.

Make validated Application input expose required normalized values and explicit account/party variants. Resolve treasury/account policy once before protocol mapping. Do not leak generated models into Application or add a generic message framework.

Serialization changes require independent semantic fixtures and signature verification. Canonicalization does not make arbitrary post-signing edits harmless. Recommendation: serialize/sign once, then persist and reuse the exact artifact. [XML Signature](https://www.w3.org/TR/xmldsig-core1/), [Canonical XML](https://www.w3.org/TR/xml-c14n11/)

## Application validation

FluentValidation provides typed property rules and child validators. Child validators skip null children, so required objects need explicit presence checks. Keep cross-field/policy rules in the payment validator and checksum algorithms in small tested functions. [Composition](https://docs.fluentvalidation.net/en/latest/start.html)

Invoke validation explicitly at the Application boundary. Manual invocation is documented; the older MVC auto-validation pipeline is not recommended for new projects and is synchronous. Explicit Application invocation also works for non-HTTP callers. [Integration guidance](https://docs.fluentvalidation.net/en/latest/aspnet.html)

FluentValidation 12 requires .NET 8 or later, fitting .NET 10. Its addition still requires a reviewed change to the repository's Application package rule; no ASP.NET integration package is needed. Concrete composed validators using the existing result type are the dependency-free alternative. [Version requirements](https://docs.fluentvalidation.net/en/latest/upgrading-to-12.html)

Preserve error paths, accumulation, normalization, amount/time boundaries, optional-object semantics, treasury exceptions, and collection errors with characterization tests. Library defaults must not change external behavior silently. Construct normalized validated input only after success. Domain transitions and Infrastructure schema checks protect separate invariants.

## Implementation outcome

The owner approved implementation. The pinned dotnet-xscgen 3.0.1405 tool ran successfully against both imported schemas with explicit namespaces, element ordering, direct names and disabled interface/comment generation. It produced 47,406 bytes for the header and 152,069 bytes for pacs.008. Its DateTime and optional Specified properties would require extra handling for the exact lexical profile. We selected authored, focused XmlSerializer models with explicit order/namespaces and lexical date/amount properties instead of committing the full generated surface. Independent XML expectations and the original XSD checks remain authoritative. Trial tooling/output is ignored scratch work, not a build dependency.

Core FluentValidation 12.1.1 is pinned and explicitly allowed in Application. Composed validators feed the normalized immutable payment factory. No change to the Application project-reference boundary or Contracts was needed. See the architecture, specification and review for the accepted implementation and verification.
