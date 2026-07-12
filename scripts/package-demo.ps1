[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [ValidateSet('win-x64')]
    [string] $Runtime = 'win-x64',

    [string] $OutputDirectory = 'artifacts/release',

    [switch] $NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repositoryRoot 'apps/PadesSharpDemoApp/PadesSharpDemoApp.csproj'
$outputRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
$publishDirectory = Join-Path $outputRoot "publish-$Runtime"
$stageDirectory = Join-Path $outputRoot "PadesSharpDemoApp-$Version-$Runtime"
$archivePath = Join-Path $outputRoot "PadesSharpDemoApp-$Version-$Runtime.zip"
$checksumPath = "$archivePath.sha256"

foreach ($path in @($publishDirectory, $stageDirectory)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}
foreach ($path in @($archivePath, $checksumPath)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Force
    }
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $stageDirectory -Force | Out-Null

$publishArguments = @(
    'publish', $projectPath,
    '--configuration', 'Release',
    '--runtime', $Runtime,
    '--self-contained', 'true',
    '--output', $publishDirectory,
    '-p:PublishSingleFile=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    "-p:Version=$Version"
)
if ($NoRestore) { $publishArguments += '--no-restore' }

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Get-ChildItem -LiteralPath $publishDirectory -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $stageDirectory -Recurse -Force
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $stageDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md') -Destination $stageDirectory

$readme = @"
PadesSharp Demo App $Version ($Runtime)

1. Extract the complete ZIP archive.
2. Run PadesSharpDemoApp.exe on Windows 10/11 x64.
3. Windows SmartScreen may warn because this preview executable is not code-signed.
4. PKCS#11 tokens and HSMs still require the vendor's Windows driver.

This is preview software. Review SECURITY.md in the source repository before use.
Corresponding source: https://github.com/tuantafz/PadesSharp/tree/v$Version
Issues: https://github.com/tuantafz/PadesSharp/issues
License: LGPL-2.1-or-later; see LICENSE and THIRD-PARTY-NOTICES.md.
"@
[System.IO.File]::WriteAllText(
    (Join-Path $stageDirectory 'README.txt'),
    $readme,
    [System.Text.UTF8Encoding]::new($false))

$forbidden = Get-ChildItem -LiteralPath $stageDirectory -Recurse -File | Where-Object {
    $_.Extension -in @('.pdb', '.lscache', '.user', '.pfx', '.p12', '.key', '.pem')
}
if ($forbidden) {
    throw "Forbidden release files found: $($forbidden.FullName -join ', ')"
}

$executable = Join-Path $stageDirectory 'PadesSharpDemoApp.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw 'Published package does not contain PadesSharpDemoApp.exe.'
}

Compress-Archive -LiteralPath $stageDirectory -DestinationPath $archivePath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumLine = "$hash  $([System.IO.Path]::GetFileName($archivePath))`n"
[System.IO.File]::WriteAllText($checksumPath, $checksumLine, [System.Text.UTF8Encoding]::new($false))

Write-Host "Release archive: $archivePath"
Write-Host "SHA-256 file:   $checksumPath"
Write-Host "SHA-256:        $hash"
