# Signature Validation Backlog

This document records security-sensitive validation work for the public preview.
An item is not considered resolved until the current implementation is reviewed
and a regression test covers the expected behavior.

## Parser and signature discovery

- Confirm that signature discovery handles CRLF, arbitrary legal whitespace,
  indirect objects, xref streams, object streams, and incremental revisions.
- Do not treat signature-like text inside content streams as signature fields.
- Resolve field names and signature dictionaries through PDF object structure.
- Add multi-signature files produced by independent tools to compatibility tests.

## ByteRange and revision integrity

- Reject negative values, integer overflow, overlapping segments, out-of-range
  offsets, and malformed gaps.
- Ensure the excluded ByteRange gap corresponds exactly to the signature contents.
- Detect bytes appended outside the signed revision and report modification policy
  separately from cryptographic validity.

## CMS and timestamps

- Bind every RFC 3161 message imprint to the original CMS signature value.
- Validate TSA signature, EKU, certificate validity at generation time, trust
  chain, and revocation according to caller policy.
- Treat a missing or invalid required timestamp as a validation failure.
- Reject unsupported or inconsistent digest algorithms safely.

## Certificate trust and revocation

- Keep cryptographic integrity, certificate trust, and revocation status as
  separate structured results.
- Do not silently treat missing revocation clients or unknown status as valid when
  strict policy is enabled.
- Validate intermediate certificates as required by policy.
- Never infer trust merely because a certificate is self-signed.

## DSS/VRI and offline validation

- Parse embedded certificates, OCSP responses, and CRLs from DSS/VRI structures.
- Match validation material to the correct signature and certificate.
- Validate embedded evidence cryptographically before using it.
- Define deterministic preference and fallback rules for embedded versus online
  revocation sources.

## Test requirements

- Malformed and adversarial ByteRange values
- Incorrect timestamp message imprint
- Expired, untrusted, or revoked TSA certificates
- Missing TSA certificate in the token
- Unknown and unavailable revocation status under strict and permissive policy
- Multi-revision and multi-signature PDFs
- Xref streams and object streams
- Embedded DSS validation without network access
- PDFs produced by independent signing tools

## Completion criteria

For every item, document the policy behavior, add positive and negative tests,
verify that errors are structured rather than parsed from display strings, and
update README/security guidance where user expectations change.
