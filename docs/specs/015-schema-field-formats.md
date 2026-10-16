# Specification 015: field formats follow the ISO 20022 schemas

Status: owner decision of 2026-10-09, implemented on codex/proxy-completion.

## Decision

Where our request validation and the message schemas (pacs.008.001.12, pacs.009.001.11, pacs.004.001.13, pacs.002.001.14, camt.056, camt.029, pain.002) disagree on a field's format, the schema wins. Only formats change; the Annex D business rules stay.

## Formats

- `MaxNText` fields (identifiers, names, addresses, remittance, additional information, purpose and reason codes, geolocation): any character XML 1.0 can carry, 1 to N long. Before, identifiers were limited to ASCII and free text to ASCII plus Georgian, some fields to a smaller punctuation set, and reason and category purpose codes to `[A-Z0-9]`. Control characters other than tab, line feed and carriage return are still refused, because XML cannot carry them.
- Patterns stay only where the schema has one: BIC, currency, IBAN, country.
- Exceptions:
  - `ClientReference` is not a schema field. It travels in the `Idempotency-Key` HTTP header of status callbacks, so it stays printable ASCII, 1 to 35 long.
  - The Annex D channel and instrument code (four upper-case letters, 3.2.1.e) and the MCC reference (four digits) stay.
- Lengths that cover several schema occurrences are unchanged: address lines 7 × 70, structured additional information 3 × 140, unstructured remittance split into 140-character lines.

## Unchanged business rules (Annex D)

NbOfTxs = 1, the INST service level and local instrument, SLEV, amount greater than zero with at most 5 decimals, the acceptance time window, initiated-payment prefixes, debtor and creditor accounts differing, and the configured currency list and limits. IPS rejects messages that break them (for example 1002 FF01, 1015 TM01).

## Tests

- Unit: Georgian, punctuation, lowercase codes and tab or line feed are accepted; 36 characters and a control character are refused; a non-ASCII client reference is refused. Cases that expected the old character sets now use lengths or control characters.
- Integration: a pacs.008 with a 35-character Georgian instruction id, an end-to-end id containing `€<&>` and a Georgian debtor name is schema-valid, signs and verifies, and keeps the text exactly.

## Limits

Proxy (acmt.022) request formats are not part of this decision. Whether the real IPS accepts non-ASCII identifiers has not been tried against the IPS test environment.
