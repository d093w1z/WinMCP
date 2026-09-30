# M11 spike: builds bin\ProbeHook.dll (MFC extension DLL) and bin\Injector.exe. Research only; not part of CI.
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.ATLMFC -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1
foreach ($project in 'ProbeHook\ProbeHook.vcxproj', 'Injector\Injector.vcxproj') {
    & $msbuild (Join-Path $PSScriptRoot $project) -nologo -verbosity:minimal -p:Configuration=Release -p:Platform=x64
    if ($LASTEXITCODE -ne 0) { throw "MSBuild failed for $project" }
}
