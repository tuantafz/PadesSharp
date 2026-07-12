# Build & Test Guide

This document explains how to set up, build, run tests, and execute the sample projects for **PadesSharp**.

---

## Prerequisites

| Requirement | Version | Notes |
|---|---|---|
| .NET SDK | **9.0.201** or later 9.x minor | Pinned in `global.json` (`rollForward: latestMinor`) |
| Git | any recent | To clone the repository |
| SoftHSM2 *(optional)* | 2.x | Required only for Sample 05 (PKCS#11/HSM) |

Verify your SDK version:

```bash
dotnet --version
# expected: 9.0.xxx
```

If the version is lower than 9.0.201, download the SDK from  
<https://dotnet.microsoft.com/download/dotnet/9.0>

---

## Clone

```bash
git clone https://github.com/tuantafz/PadesSharp.git
cd PadesSharp
```

---

## Restore packages

All package versions are managed centrally in `Directory.Packages.props`  
(`ManagePackageVersionsCentrally = true`). A single restore command handles every project:

```bash
dotnet restore PadesSharp.sln
```

---

## Build

### Build the entire solution (all configurations)

```bash
dotnet build PadesSharp.sln
```

### Build in Release mode

```bash
dotnet build PadesSharp.sln -c Release
```

### Build a specific project

```bash
dotnet build src/ModernPdf.Signing/ModernPdf.Signing.csproj
```

### Target frameworks

| Project group | Target frameworks |
|---|---|
| `src/` libraries | `net48`, `netstandard2.0`, `net8.0` |
| `tests/` | `net8.0` |
| `samples/` | `net8.0` |

---

## Run unit tests

### Run all 177 tests

```bash
dotnet test PadesSharp.sln
```

### Verbose output (shows each test name)

```bash
dotnet test PadesSharp.sln -v normal
```

### Filter by category or name

```bash
# Run only validation tests
dotnet test tests/ModernPdf.Tests.Unit --filter "FullyQualifiedName~Validation"

# Run only performance tests
dotnet test tests/ModernPdf.Tests.Unit --filter "FullyQualifiedName~Performance"

# Run a single test by name
dotnet test tests/ModernPdf.Tests.Unit --filter "DisplayName=Sign_BasicRequest_Success"
```

### Collect code coverage (requires `coverlet`)

```bash
dotnet test PadesSharp.sln --collect:"XPlat Code Coverage"
```

Coverage reports are written to `TestResults/<guid>/coverage.cobertura.xml`.  
Use [ReportGenerator](https://github.com/danielpalme/ReportGenerator) to render HTML:

```bash
dotnet tool install -g dotnet-reportgenerator-globaltool   # once
reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coverage-report" -reporttypes:Html
```

---

## Run sample projects

Each sample is a self-contained console application under `samples/`.  
Samples 01–04 and 06 run without any external dependencies.

```bash
# Sample 01 — Basic RSA software signing
dotnet run --project samples/Sample01.BasicSigning

# Sample 02 — Sign to / validate from file
dotnet run --project samples/Sample02.FileHelper

# Sample 03 — PAdES-T (RFC 3161 timestamp)
dotnet run --project samples/Sample03.Tsa

# Sample 04 — DSS/VRI incremental append (PAdES-LTV)
dotnet run --project samples/Sample04.LtvDss

# Sample 05 — PKCS#11 / HSM (requires SoftHSM2 — see section below)
dotnet run --project samples/Sample05.Pkcs11Hsm

# Sample 06 — Validate signature + tamper detection
dotnet run --project samples/Sample06.Validation
```

Output PDFs are written next to each sample's binary (inside `bin/Debug/net8.0/`).

---

## Sample 05 — SoftHSM2 setup (optional)

Sample 05 (`Sample05.Pkcs11Hsm`) requires a PKCS#11 library and an initialised token.

### Install SoftHSM2

**Windows**

```powershell
winget install OpenSC.SoftHSM2
```

Default library path after installation:  
`C:\Program Files\SoftHSM2\lib\softhsm2-x64.dll`

**Linux (Debian/Ubuntu)**

```bash
sudo apt install softhsm2
```

Library path: `/usr/lib/softhsm/libsofthsm2.so`

**macOS (Homebrew)**

```bash
brew install softhsm
```

Library path: `/opt/homebrew/lib/softhsm/libsofthsm2.so`

### Initialise a token and generate a key

```bash
softhsm2-util --init-token --slot 0 --label "PadesSharp" --pin 1234 --so-pin 0000

pkcs11-tool --module <path-to-libsofthsm2> \
            --slot 0 --login --pin 1234 \
            --keypairgen --key-type rsa:2048 \
            --label "SignKey" --id 01
```

### Configure environment variables

```bash
# Windows PowerShell
$env:PKCS11_LIB   = "C:\Program Files\SoftHSM2\lib\softhsm2-x64.dll"
$env:PKCS11_PIN   = "1234"
$env:PKCS11_SLOT  = "0"
$env:PKCS11_LABEL = "SignKey"

# Linux / macOS
export PKCS11_LIB=/usr/lib/softhsm/libsofthsm2.so
export PKCS11_PIN=1234
export PKCS11_SLOT=0
export PKCS11_LABEL=SignKey
```

Then run the sample:

```bash
dotnet run --project samples/Sample05.Pkcs11Hsm
```

If the library is not found the sample exits gracefully with an informational message.

---

## Clean build artefacts

```bash
dotnet clean PadesSharp.sln
```

To remove all `bin/` and `obj/` directories manually:

```bash
# PowerShell
Get-ChildItem -Recurse -Include bin,obj -Directory | Remove-Item -Recurse -Force

# bash
find . -type d \( -name bin -o -name obj \) | xargs rm -rf
```

---

## Common issues

| Problem | Cause | Fix |
|---|---|---|
| `SDK version mismatch` | Installed SDK < 9.0.201 | Install .NET 9 SDK from microsoft.com/download |
| `Encoding.Latin1` not found | Building on .NET Framework 4.8 | The codebase uses `Encoding.GetEncoding(28591)` — do not change this |
| `PKCS#11 library not found` | SoftHSM2 not installed or wrong path | Set `PKCS11_LIB` environment variable to the correct library path |
| Tests fail with `ObjectDisposedException` | Disposed resource reuse in test teardown | Ensure test fixtures implement `IDisposable` and clean up `Pkcs11SessionPool` |
| Coverage report empty | `coverlet.collector` not referenced | Package is declared in `Directory.Packages.props` and referenced in the test project |
