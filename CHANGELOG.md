# Changelog

## [Unreleased]

### Changed
- Prepared the repository for its first public preview.
- Added security and third-party attribution documentation.
- Marked the package version as `0.1.0-preview.1`.

## v0.1.0-preview.1 (unreleased)

### Features
- PDF signing with SHA-256 / SHA-384 / SHA-512
- CMS detached signature (PKCS#7 / CAdES-BES)
- Visible signature appearance (text, logo, Unicode)
- RFC 3161 TSA client (PAdES-T)
- OCSP / CRL revocation (PAdES-LTV)
- DSS / VRI incremental append
- PKCS#11 / HSM signing adapter (session pool, reconnect)
- PDF signature validation (integrity, cert, timestamp)
- File-based signing / validation helpers
- Multi-threaded concurrent signing

### Samples
- 6 console app samples covering all features
- WinForms demo app (batch signing, TSA, LTV, settings persistence)

### LegacyPdfCore
- Imported iTextSharp 4.1.6 LGPL source (424 files) into `src/LegacyPdfCore/`
- Upgraded from net48-only NuGet reference to source build with multi-target (net48, netstandard2.0, net8.0)
- Fixed BouncyCastle 2.x API incompatibilities (AESCipher, PdfPKCS7, OcspClient, PdfPublicKeySecurityHandler)
- Added `System.Drawing.Common` dependency for .NET Core
- Removed `iTextSharp-LGPL` NuGet dependency entirely

### Build
- Multi-target: net48, netstandard2.0, net8.0
- Docker multi-stage build
- LGPL 2.1+ licensed
