# Specification 016: one bank identity in configuration

Status: owner decisions of 2026-10-12; specification approved and implemented on codex/proxy-completion the same day. Commit and merge approval pending.

## Decision

The gateway serves one bank. Its own code, the National Bank of Georgia (NBG) code and the Treasury code are configured once and every component reads them from there:

```json
"Bank": {
  "BicCode": "ITSBGE22",
  "NbgCode": "GEATSAAA",
  "TreasuryCode": "TRESGE22"
}
```

- `BicCode`: the bank that owns the system. Every outgoing message is sent as this bank, and every incoming payment must be addressed to it.
- `NbgCode`: NBG, which operates the IPS and the Proxy Solution. It is the counterparty in the message header (`To` on outgoing, `Fr` on incoming replies) and the assignee of acmt.022.
- `TreasuryCode`: the Treasury. It is optional, as `TreasuryBic` is today.

NBG and the Treasury get separate validation rules. Today only the Treasury has them, in the outgoing pacs.008 creditor rules. A later rule for either one reads the same identity and adds nothing to the configuration.

## Settings replaced

| Removed | Now read from |
| --- | --- |
| `Proxy:ParticipantBic` | `Bank:BicCode` |
| `Payments:Outgoing:Transport:ParticipantBic` | `Bank:BicCode` |
| `Payments:Incoming:Transport:ParticipantBic` | `Bank:BicCode` |
| `Proxy:ProxyBic` | `Bank:NbgCode` |
| `Payments:Outgoing:Protocol:IpsBic` | `Bank:NbgCode` |
| `Payments:Incoming:Protocol:IpsBic` | `Bank:NbgCode` |
| `Payments:Outgoing:Policy:TreasuryBic` | `Bank:TreasuryCode` |

The values are not copied over from the old keys. If a removed key is still set, startup fails and names the key, so a stale deployment setting is noticed instead of ignored. The protocol sections keep `ServiceLevelCode` and `RemittanceMethod`.

## Validation at startup

When any feature that uses the bank identity is enabled (outgoing execution, incoming workers or the proxy), startup checks the following:

- `BicCode` and `NbgCode` are required, and each is an 8- or 11-character BIC.
- `TreasuryCode` is either absent or a valid BIC.
- No two of the three codes are equal (case-insensitive).
- All three are stored in upper case.

## Code shape

- Application gets one immutable `BankIdentity(Bic, NbgBic, TreasuryBic?)` with `IsOwn`, `IsNbg` and `IsTreasury` (ordinal, case-insensitive, trimmed).
- `Pacs008Policy` takes the `BankIdentity` instead of separate participant and treasury strings. The existing `IsTreasury` rule keeps its behavior.
- `Pacs008ProtocolProfile` and `IpsMessageProfile` take the NBG code from the identity. Message builders are unchanged.
- `ProxySettings` and the transport settings lose their BIC fields. Infrastructure reads the bank code from the identity for the `Channel` header, the `RequireParticipant` check and the acmt.022 agents.
- Api binds `Bank` once and registers `BankIdentity` as a singleton. The protocol sections are bound to `ServiceLevelCode` and `RemittanceMethod` only, and the profiles are built with `Bank:NbgCode`. The configuration reader already rejects unknown keys, so a removed key fails startup and the error names it.

## Incoming pacs.008 addressed to another bank

Owner decision: this is rejected (RJCT). It is not expected to happen in practice.

- After the signature and schema checks pass, `IncomingReceiptPreparation` (Application) compares the creditor agent (`CdtrAgt/FinInstnId/BICFI`) with the receipt participant. The receipt participant is `Bank:BicCode`, because the transport refuses any other participant. If they differ, the result becomes a `Reject` with reason code `RC07` (ISO 20022 "Invalid creditor BIC identifier") and the description "Creditor agent '<code>' is not this bank." The check sits beside the existing "NotOurs" check of the other incoming types, so the protocol reader stays free of business decisions.
- From there it follows the existing FF01 path: the original references are staged, a signed pacs.002 RJCT is sent, and no payment is registered and the core banking system (CBS) is not called. The reply repository, which allowed a reply without a payment only for FF01, now allows exactly FF01 and RC07.
- An untrusted or malformed message is still held, as it is today. The check runs only on trusted content.
- The other incoming types keep their existing "NotOurs" hold. Changing them is not part of this decision.

## Tests

- Unit: `BankIdentity` matching and normalization. Configuration validation rejects each of: a missing code, an invalid code, equal codes, and a removed key that is still set.
- Unit: a pacs.008 creditor that is the Treasury is still accepted. The policy built from the identity behaves as before.
- Integration: a signed incoming pacs.008 whose `CdtrAgt` is another bank produces a signed pacs.002 RJCT `RC07` and no registered payment.
- Integration: the outgoing pacs.008, the incoming reply and the acmt.022 carry `BicCode` and `NbgCode` in the agent, header and `Channel` fields.
- Existing tests and the Aspire AppHost move to the `Bank` section with the same values they use today.

## Compatibility

The Contracts package, the JSON contracts, the database schema and the events are unchanged, and no migration is needed. Deployment configuration must move to the `Bank` section. `appsettings.json` ships with ITSBGE22, GEATSAAA and TRESGE22.

## Open points

- `RC07` is the ISO code. If the IPS rules (Annex D/E) require a different reason code for an unknown creditor agent, use that code instead.
