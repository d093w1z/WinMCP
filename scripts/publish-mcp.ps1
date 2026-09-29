<#
.SYNOPSIS
Publishes WinMcp.Server for MCP clients. Safe to run while a client is using the previous build.

.DESCRIPTION
.mcp.json runs artifacts\winmcp\WinMcp.Server.exe. artifacts\winmcp is a directory junction to the newest build in
artifacts\winmcp-builds\<timestamp>. Each publish goes to a new folder and then re-points the junction, so running
servers keep using their own folder (Windows won't let in-use files be overwritten) and new sessions get the new
build. Older builds are deleted once nothing has files open in them. Reconnect the server in your client (e.g.
/mcp in Claude Code) to pick up the new build.
#>
param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$builds = Join-Path $root 'artifacts\winmcp-builds'
$link = Join-Path $root 'artifacts\winmcp'
$target = Join-Path $builds (Get-Date -Format 'yyyyMMdd-HHmmss')

dotnet publish (Join-Path $root 'src\WinMcp.Server') -c $Configuration -o $target --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

if (Test-Path $link) {
    if (-not ((Get-Item $link -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "$link exists and is not a junction; remove it manually."
    }
    cmd /c rmdir "$link" | Out-Null   # removes the junction only, never the build it points to
}
New-Item -ItemType Junction -Path $link -Target $target | Out-Null
Write-Host "Published to $target; artifacts\winmcp now points there."

# A folder with open files can't be renamed, which makes a rename the reliable "is anything using this?" test.
foreach ($old in Get-ChildItem $builds -Directory | Where-Object { $_.FullName -ne $target }) {
    $probe = "$($old.FullName).deleting"
    try {
        Rename-Item $old.FullName $probe -ErrorAction Stop
        Remove-Item $probe -Recurse -Force
        Write-Host "Removed unused build $($old.Name)."
    }
    catch {
        Write-Host "Kept build $($old.Name): still in use by a running server."
    }
}
