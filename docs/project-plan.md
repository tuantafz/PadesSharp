# PadesSharp Engineering Plan

This document summarizes the architecture and public roadmap. Completed behavior
is described by source code and tests; roadmap entries are not release promises.

## Goals

- Provide a modular PDF-signing API for .NET Framework 4.8, .NET Standard 2.0,
  and .NET 8.
- Support software keys and external signers such as PKCS#11 tokens and HSMs.
- Implement CMS/CAdES signing, RFC 3161 timestamps, revocation collection, and
  PAdES DSS/VRI updates.
- Validate signatures using explicit trust, timestamp, and revocation policies.
- Preserve clear license provenance for the legacy PDF layer.

## Architecture

| Module | Responsibility |
|---|---|
| `ModernPdf.Abstractions` | Stable interfaces, requests, results, and policies |
| `ModernPdf.Crypto` | Digests, CMS, TSA, OCSP, CRL, and software signing |
| `ModernPdf.Appearance` | Visible signature appearance generation |
| `ModernPdf.Signing` | PDF ByteRange preparation and CMS injection |
| `ModernPdf.Pades` | DSS/VRI collection and incremental updates |
| `ModernPdf.Pkcs11` | PKCS#11 sessions, pooling, and signing adapters |
| `ModernPdf.Validation` | PDF signature extraction and policy-based validation |
| `LegacyPdfCore` | LGPL low-level PDF read/write implementation |

The signing layer depends on abstractions rather than private-key implementations.
External signers receive the exact digest/signing input required by the selected
mechanism and return only signature bytes.

## Implemented milestones

- Multi-target solution and deterministic build
- SHA-256, SHA-384, and SHA-512 digest services
- Detached CMS/CAdES signing
- Incremental PDF signing with ByteRange handling
- Visible signature appearances
- RFC 3161 timestamp client
- OCSP and CRL clients
- DSS/VRI embedding
- PKCS#11 session pooling and reconnect handling
- File helpers, concurrent signing, and validation APIs
- Unit, integration, and compatibility test projects

## Public-preview priorities

1. Keep validation fail-closed under strict policy.
2. Expand third-party PDF compatibility coverage.
3. Verify timestamps, certificate chains, EKU, validity periods, and message
   imprints independently.
4. Validate all ByteRange arithmetic and signed revision boundaries safely.
5. Prefer embedded DSS/VRI material for offline validation when policy allows it.
6. Test multi-signature incremental PDFs and xref/object streams.
7. Publish reproducible packages with symbols and source links.
8. Document platform limitations, especially System.Drawing and PKCS#11 drivers.

## Release criteria

A preview or stable release requires:

- Clean restore, Release build, and tests on supported CI platforms
- No committed secrets, private keys, or generated build output
- Package-content inspection and a clean consumer-project installation test
- Updated changelog, security guidance, and third-party notices
- Regression tests for every resolved validation or signing defect
- Explicit review of breaking API and security-policy changes

## Non-goals for the preview

- Legal or regulatory certification of a signature
- Automatic trust decisions without caller-provided policy
- Vendor-specific HSM provisioning or certificate lifecycle management
- Guaranteed parsing of every malformed or proprietary PDF variant

See `issues/` for the active engineering backlog.
