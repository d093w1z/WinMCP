<#
.SYNOPSIS
Builds samples\WinMcp.MfcTestApp (C++/MFC). Needs Visual Studio with "Desktop development with C++" and the MFC
component for the default toolset. Not part of `dotnet build`; integration tests skip the MFC cases when it isn't built.

Produces two variants: bin\x64\<Configuration> (shared MFC DLL, the usual case) and bin\x64\<Configuration>Static
(MFC linked statically, for framework-detection tests; skip it with -SharedOnly).
#>
param([string]$Configuration = 'Release', [switch]$SharedOnly)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'Visual Studio not found (no vswhere.exe).' }

$msbuild = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 Microsoft.VisualStudio.Component.VC.ATLMFC `
    -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'No Visual Studio installation with C++ build tools and MFC was found.' }

$project = Join-Path $root 'samples\WinMcp.MfcTestApp\WinMcp.MfcTestApp.vcxproj'
$projectDir = Split-Path -Parent $project

& $msbuild $project -nologo -verbosity:minimal "-p:Configuration=$Configuration" '-p:Platform=x64'
if ($LASTEXITCODE -ne 0) { throw "MSBuild failed with exit code $LASTEXITCODE" }
Write-Host "Built samples\WinMcp.MfcTestApp\bin\x64\$Configuration\WinMcp.MfcTestApp.exe"

if (-not $SharedOnly) {
    $variant = "${Configuration}Static"
    & $msbuild $project -nologo -verbosity:minimal "-p:Configuration=$Configuration" '-p:Platform=x64' '-p:UseOfMfc=Static' `
        "-p:OutDir=$projectDir\bin\x64\$variant\" "-p:IntDir=$projectDir\obj\x64\$variant\"
    if ($LASTEXITCODE -ne 0) { throw "MSBuild (static MFC) failed with exit code $LASTEXITCODE" }
    Write-Host "Built samples\WinMcp.MfcTestApp\bin\x64\$variant\WinMcp.MfcTestApp.exe"
}
