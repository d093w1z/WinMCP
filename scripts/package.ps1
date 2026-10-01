<#
.SYNOPSIS
Builds the release archive: a self-contained, single-file WinMcp.Server.exe (no .NET install needed on the target
machine) plus the user documentation, as artifacts\release\WinMCP-<version>-<runtime>.zip with a SHA-256 checksum.

.EXAMPLE
.\scripts\package.ps1                 # win-x64, version from Directory.Build.props
#>
param(
    [string]$Runtime = 'win-x64',
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
$name = "WinMCP-$Version-$Runtime"
$staging = Join-Path $root "artifacts\package\$name"
$release = Join-Path $root 'artifacts\release'

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force $staging, $release | Out-Null

# Single file, self-contained, compressed. Not trimmed: UI Automation is COM interop, which trimming breaks.
dotnet publish (Join-Path $root 'src\WinMcp.Server\WinMcp.Server.csproj') -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None -p:Version=$Version -o $staging --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$docs = New-Item -ItemType Directory -Force (Join-Path $staging 'docs')
Copy-Item (Join-Path $root 'README.md') $staging
foreach ($doc in 'tools.md', 'security.md', 'limitations.md') {
    Copy-Item (Join-Path $root "docs\$doc") $docs
}

$zip = Join-Path $release "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $name.zip" -Encoding ascii

Write-Host "Packaged $zip"
Write-Host "SHA-256  $hash"
