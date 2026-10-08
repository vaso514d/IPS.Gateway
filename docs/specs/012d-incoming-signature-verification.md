# Specification 012d: IPS signature verification follows configuration

Status: owner decisions of 2026-10-08, implemented on codex/proxy-completion.

## Decision

The gateway's job is to sign the messages it sends. Whether it also checks IPS's signature depends only on configuration: if NBG provides the IPS signing certificate (NETCMNUB.cer, Annex D 5.2) and it is configured as `IpsSignatureTrust`, every IPS signature is verified with it; if no certificate is configured, nothing is verified and only the content of the message decides. There is no separate switch.

This applies per direction:

- `Payments:Incoming:Transport:IpsSignatureTrust`: incoming messages (pacs.008, pacs.009, pacs.004, pain.001, camt.056, camt.055, camt.029, unsolicited pacs.002).
- `Payments:Outgoing:Transport:IpsSignatureTrust`: IPS's pacs.002 replies to our sends and pacs.028 answers.

Both lists are now optional; an entry that names no source still fails startup.

## Finding the IPS certificate (Annex C 2.2, 2.6)

IPS does not embed its certificate; its signature names it by `X509IssuerSerial` (issuer name and serial number), and the participant finds the public certificate in its own store. The verifier previously required an embedded `X509Certificate` and would have refused every real IPS signature. It now picks the candidate certificates in this order:

1. an embedded `X509Certificate`: only a configured certificate with the same bytes;
2. `X509IssuerSerial`: configured certificates with that serial number (decimal) and that issuer, compared attribute by attribute ignoring spacing and case, so both Java (RFC 2253) and .NET forms match;
3. no certificate named: every configured certificate, as the central system does with a participant's list.

The signature must verify with a candidate; the certificate it verifies with must be valid at the time of verification (012b), otherwise the message is reported as signed outside the certificate's validity. A signature that verifies with none is untrusted. The rest of the profile (C14N 1.1 SignedInfo, enveloped + C14N 1.0, SHA-256, ECDSA-SHA256, one signature in `AppHdr/Sgntr`) is unchanged.

Behavior difference from 012b: the validity period is judged after the signature verifies, so a tampered message signed by an expired certificate is now reported as untrusted rather than as outside validity.

## Tests

`IpsSignatureVerificationTests` (no Java needed; messages signed by our signer with the KeyInfo rewritten, which is outside SignedInfo): without a certificate any or no valid signature is accepted and only content decides; with one, an issuer/serial (.NET and RFC 2253 issuer), no-KeyInfo and embedded signature verifies, a change after signing does not, another certificate does not, a wrong serial or issuer does not, and an expired certificate is reported as such. `OutgoingTransportConfigurationTests`: each direction starts with or without its certificate.

## Limits

Nothing is verified against a real IPS message yet. Without a configured certificate, a message injected on the mutually authenticated TLS channel would not be stopped by its signature.
