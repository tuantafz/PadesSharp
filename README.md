# PadesSharp

PadesSharp is a preview-stage PDF digital-signature library for .NET. Its modern
signing modules are implemented from public standards including ISO 32000,
RFC 5652, RFC 3161, RFC 6960, and ETSI PAdES specifications. The low-level PDF
layer is a multi-target source fork of iTextSharp 4.1.6 under LGPL-2.1-or-later.

> **Preview notice:** APIs and validation behavior may change before v1.0.
> Review [SECURITY.md](SECURITY.md) and the known validation limitations before
> using this library in production or compliance-sensitive workflows.

## Features

- PDF signing with SHA-256, SHA-384, and SHA-512
- Detached CMS / PKCS#7 and CAdES-BES signatures
- Visible signature appearances with text, images, Unicode, and page rotation
- RFC 3161 timestamp authority client for PAdES-T
- OCSP and CRL clients
- DSS/VRI incremental updates for PAdES-LTV
- PKCS#11/HSM signing with session pooling and reconnect support
- Signature validation for document integrity, CMS, certificates, timestamps,
  revocation data, and embedded DSS data
- File helpers and concurrent signing support

## Requirements

- .NET SDK 9.0.201 or a compatible later 9.x SDK; see `global.json`
- Library targets: .NET Framework 4.8, .NET Standard 2.0, and .NET 8
- A vendor PKCS#11 driver is required only when signing with an HSM or token

## Download the demo app

Windows users can download the latest packaged demo from
[GitHub Tags](https://github.com/tuantafz/PadesSharp/tags).

Download `PadesSharpDemoApp-<version>-win-x64.zip`, extract the complete archive,
and run `PadesSharpDemoApp.exe`. The package is self-contained, so a separate .NET
runtime installation is not required. Windows SmartScreen may display a warning
because preview executables are not yet code-signed. HSM and USB-token use still
requires the device vendor's Windows driver.

Use the accompanying `.sha256` file to verify the archive before running it.

## Installation

NuGet packages have not been published yet. Clone the repository and use project
references during the preview period:

```bash
git clone https://github.com/tuantafz/PadesSharp.git
cd PadesSharp
dotnet build PadesSharp.sln --configuration Release
```

See [docs/install.md](docs/install.md) for the complete build and test guide.

## Quick start

```csharp
using System.Security.Cryptography.X509Certificates;
using ModernPdf.Abstractions.Crypto;
using ModernPdf.Abstractions.Signing;
using ModernPdf.Crypto;
using ModernPdf.Signing;

using var certificate = new X509Certificate2(
    "signer.p12",
    "password",
    X509KeyStorageFlags.Exportable);
using var provider = new RsaSoftwareSignatureProvider(certificate);

var digestService = new DefaultDigestService();
var cmsSigner = new BouncyCastleCmsSigner(digestService);
var engine = new PdfSigningEngine(cmsSigner, digestService);

await using var output = File.Create("signed.pdf");
var result = await engine.SignAsync(new PdfSignRequest
{
    OutputPdf = output,
    Certificate = certificate,
    SignatureProvider = provider,
    DigestAlgorithm = PdfDigestAlgorithm.Sha256,
    Reason = "Approved",
    Location = "Hanoi",
    SignatureName = "Signature1"
});
```

Never embed real certificate passwords, token PINs, or private keys in source code.
Use a secure secret provider in production.

## Validation

```csharp
using ModernPdf.Validation;

var validator = new DefaultPdfSignatureValidator();
var report = validator.Validate(File.OpenRead("signed.pdf"));

Console.WriteLine($"Valid: {report.IsValid}");
foreach (var signature in report.Signatures)
{
    Console.WriteLine($"{signature.SignatureName}: {signature.CmsSignatureValid}");
    foreach (var error in signature.Errors)
        Console.WriteLine($"  Error: {error}");
}
```

Validation results must be interpreted under an explicit trust and revocation
policy. A successful `IsValid` result is not, by itself, legal or regulatory
assurance.

## Samples

| Project | Purpose |
|---|---|
| `Sample01.BasicSigning` | Sign with an in-memory RSA certificate |
| `Sample02.FileHelper` | File-based signing and validation helpers |
| `Sample03.Tsa` | Add an RFC 3161 timestamp |
| `Sample04.LtvDss` | Add DSS/VRI data incrementally |
| `Sample05.Pkcs11Hsm` | Sign through SoftHSM2 or a hardware HSM |
| `Sample06.Validation` | Validate signatures and detect tampering |

Run a sample with:

```bash
dotnet run --project samples/Sample01.BasicSigning
```

## Repository layout

```text
src/       Library projects and the LegacyPdfCore source fork
tests/     Unit, integration, and compatibility tests
samples/   Standalone console samples
apps/      Windows Forms demonstration application
docs/      Build, architecture, provenance, and application documentation
issues/    Public engineering backlog and validation limitations
```

## Build and test

```bash
dotnet restore PadesSharp.sln
dotnet build PadesSharp.sln --configuration Release --no-restore
dotnet test PadesSharp.sln --configuration Release --no-build
```

All tests must finish with zero failures before publishing a release.

To build the same self-contained ZIP used by GitHub Releases:

```powershell
./scripts/package-demo.ps1 -Version 0.1.0-preview.1
```

## Main dependencies

| Component | Version | Purpose |
|---|---:|---|
| LegacyPdfCore source fork | iTextSharp 4.1.6 lineage | Low-level PDF read/write support |
| BouncyCastle.Cryptography | 2.4.0 | ASN.1, CMS, TSP, OCSP, CRL, and X.509 |
| Pkcs11Interop | 5.2.0 | PKCS#11 interoperability |
| Microsoft.Extensions.* | 8.0.x | HTTP, logging, and dependency injection abstractions |

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request. In
particular, do not copy code from iText 5/7 or any AGPL/commercial source.

## License and provenance

PadesSharp is distributed under the GNU Lesser General Public License version 2.1
or later. `LegacyPdfCore` is derived from iTextSharp 4.1.6. The `ModernPdf.*`
modules are original implementations based on public standards.

See [LICENSE](LICENSE), [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), and
[docs/license-provenance.md](docs/license-provenance.md).
