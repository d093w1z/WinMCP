<#
.SYNOPSIS
Publishes WinMcp.Server (for MCP clients) and WinMcp.TestApp (for trying it by hand). Safe while either is running.

.DESCRIPTION
.mcp.json runs artifacts\winmcp\WinMcp.Server.exe; launch the test app from artifacts\testapp\WinMcp.TestApp.exe.
Both paths are directory junctions to the newest build in artifacts\<name>-builds\<timestamp>. Each publish goes to
a new folder and then re-points the junction, so running copies keep their own folder (Windows won't let in-use files
be overwritten) and nothing ever locks the bin\ folders that builds and tests write. Older builds are deleted once
nothing has files open in them. Reconnect the server in your client (e.g. /mcp in Claude Code) to pick up a new build.
#>
param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

function Publish-Junctioned([string]$Project, [string]$Name) {
    $builds = Join-Path $root "artifacts\$Name-builds"
    $link = Join-Path $root "artifacts\$Name"
    $target = Join-Path $builds $stamp

    dotnet publish (Join-Path $root $Project) -c $Configuration -o $target --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish $Project failed with exit code $LASTEXITCODE" }

    if (Test-Path $link) {
        if (-not ((Get-Item $link -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "$link exists and is not a junction; remove it manually."
        }
        cmd /c rmdir "$link" | Out-Null   # removes the junction only, never the build it points to
    }
    New-Item -ItemType Junction -Path $link -Target $target | Out-Null
    Write-Host "Published $Name to $target; artifacts\$Name now points there."

    # A folder with open files can't be renamed, which makes a rename the reliable "is anything using this?" test.
    foreach ($old in Get-ChildItem $builds -Directory | Where-Object { $_.FullName -ne $target }) {
        $probe = "$($old.FullName).deleting"
        try {
            Rename-Item $old.FullName $probe -ErrorAction Stop
            Remove-Item $probe -Recurse -Force
            Write-Host "  removed unused build $($old.Name)"
        }
        catch {
            Write-Host "  kept build $($old.Name): still in use"
        }
    }
}

Publish-Junctioned 'src\WinMcp.Server' 'winmcp'
Publish-Junctioned 'samples\WinMcp.TestApp' 'testapp'
