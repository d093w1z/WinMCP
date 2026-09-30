<#
.SYNOPSIS
Builds samples\WinMcp.MfcTestApp (C++/MFC). Needs Visual Studio with "Desktop development with C++" and the MFC
component for the default toolset. Not part of `dotnet build`; integration tests skip the MFC cases when it isn't built.
#>
param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'Visual Studio not found (no vswhere.exe).' }

$msbuild = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 Microsoft.VisualStudio.Component.VC.ATLMFC `
    -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'No Visual Studio installation with C++ build tools and MFC was found.' }

& $msbuild (Join-Path $root 'samples\WinMcp.MfcTestApp\WinMcp.MfcTestApp.vcxproj') -nologo -verbosity:minimal `
    "-p:Configuration=$Configuration" '-p:Platform=x64'
if ($LASTEXITCODE -ne 0) { throw "MSBuild failed with exit code $LASTEXITCODE" }
Write-Host "Built samples\WinMcp.MfcTestApp\bin\x64\$Configuration\WinMcp.MfcTestApp.exe"
