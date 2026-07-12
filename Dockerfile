FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY global.json .
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY PadesSharp.sln .

# Copy project files
COPY src/LegacyPdfCore/LegacyPdfCore.csproj src/LegacyPdfCore/
COPY src/ModernPdf.Abstractions/ModernPdf.Abstractions.csproj src/ModernPdf.Abstractions/
COPY src/ModernPdf.Crypto/ModernPdf.Crypto.csproj src/ModernPdf.Crypto/
COPY src/ModernPdf.Signing/ModernPdf.Signing.csproj src/ModernPdf.Signing/
COPY src/ModernPdf.Pades/ModernPdf.Pades.csproj src/ModernPdf.Pades/
COPY src/ModernPdf.Pkcs11/ModernPdf.Pkcs11.csproj src/ModernPdf.Pkcs11/
COPY src/ModernPdf.Validation/ModernPdf.Validation.csproj src/ModernPdf.Validation/
COPY src/ModernPdf.Appearance/ModernPdf.Appearance.csproj src/ModernPdf.Appearance/
COPY src/ModernPdf.Samples.Console/ModernPdf.Samples.Console.csproj src/ModernPdf.Samples.Console/
COPY tests/ModernPdf.Tests.Unit/ModernPdf.Tests.Unit.csproj tests/ModernPdf.Tests.Unit/
COPY tests/ModernPdf.Tests.Integration/ModernPdf.Tests.Integration.csproj tests/ModernPdf.Tests.Integration/
COPY tests/ModernPdf.Tests.Compatibility/ModernPdf.Tests.Compatibility.csproj tests/ModernPdf.Tests.Compatibility/

# Restore (cached layer)
RUN dotnet restore PadesSharp.sln

# Copy sources
COPY src/ src/
COPY tests/ tests/

# Build
RUN dotnet build PadesSharp.sln --configuration Release --no-restore

# Test
RUN dotnet test PadesSharp.sln --configuration Release --no-build --verbosity normal

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=build /src/src/ModernPdf.Samples.Console/bin/Release/net8.0/ .
ENTRYPOINT ["dotnet", "ModernPdf.Samples.Console.dll"]
