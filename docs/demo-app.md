# PadesSharp Demo Application

The Windows Forms demo in `apps/PadesSharpDemoApp` demonstrates interactive and
batch PDF signing. It is a sample application, not a production certificate or
key-management product.

## Capabilities

- Select a certificate from the Windows certificate store
- Sign one or more PDF files
- Configure reason, location, signature name, and digest algorithm
- Add a visible signature with text or a logo
- Optionally request an RFC 3161 timestamp
- Optionally collect revocation data and append DSS/VRI information
- Use English or Vietnamese UI resources
- Display per-file progress and diagnostic output

## Run the application

The application targets Windows and .NET 8:

```bash
dotnet run --project apps/PadesSharpDemoApp
```

## Download a packaged release

Open [GitHub Releases](https://github.com/tuantafz/PadesSharp/releases/latest) and
download `PadesSharpDemoApp-<version>-win-x64.zip`. Extract the complete archive
before running `PadesSharpDemoApp.exe`. The package is self-contained for Windows
10/11 x64 and does not require a separate .NET runtime installation.

Verify the download with the accompanying checksum file:

```powershell
$expected = (Get-Content .\PadesSharpDemoApp-<version>-win-x64.zip.sha256).Split(' ')[0]
$actual = (Get-FileHash .\PadesSharpDemoApp-<version>-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()
$actual -eq $expected
```

SmartScreen may warn because preview builds are not code-signed. PKCS#11 devices
still require their vendor driver. The release page links to the corresponding
source tag and includes the LGPL license and third-party notices.

The selected certificate must contain or provide access to its private key. For
USB tokens and HSMs, install the vendor middleware before running the app.

## Signing workflow

1. Select the input PDF files and output directory.
2. Choose a certificate and verify that it is appropriate for digital signing.
3. Select the signing level and digest algorithm.
4. Configure the visible appearance, if required.
5. Configure TSA or revocation services only over trusted endpoints.
6. Start the batch and review the result for every file.

Cancellation is cooperative. An operation already inside a synchronous PDF or
cryptographic call may finish before cancellation takes effect.

## Settings and sensitive data

The demo may persist non-sensitive UI preferences. It must not persist certificate
passwords, private keys, token PINs, or TSA credentials. Sensitive values must not
appear in logs. Production applications should use a dedicated secret provider and
an auditable key-management design.

## Error handling

The UI reports common failures such as an inaccessible private key, invalid input
PDF, insufficient signature placeholder size, TSA failure, revocation-service
failure, and output-file access errors. Technical output is intended for diagnosis
and may change between preview releases.

## Production guidance

The demo intentionally keeps application architecture simple. A production client
should separate signing orchestration, policy, trust configuration, secret
management, logging, and UI concerns. It should also define retry and cancellation
behavior explicitly and independently validate generated signatures.
