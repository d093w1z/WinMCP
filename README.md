# WinMCP

An MCP server that gives AI agents semantic access to native Windows applications — HWND hierarchy, UI Automation tree and Win32 metadata — rather than screenshots and mouse coordinates.

> **Status:** proof of concept, under active design. See [docs/design/mvp-plan.md](docs/design/mvp-plan.md). Tools so far (all read-only): `list_windows`, `inspect_window`, `get_ui_tree`, `find_elements`.

## Requirements

- Windows 10 1809+ / Windows 11
- .NET 10 SDK (pinned in `global.json`)

## Build & test

```powershell
dotnet build WinMcp.slnx
dotnet test --solution WinMcp.slnx                                   # everything (GUI tests open windows)
dotnet test --solution WinMcp.slnx --filter-not-trait "Category=Windows" --ignore-exit-code 8   # no GUI
```

GUI tests (`Category=Windows`) launch `samples/WinMcp.TestApp` and need an unlocked, interactive desktop. Don't use the mouse or keyboard while they run.

## Running the server

```text
WinMcp.Server [--mode observe|control] [--allow <process-name-or-exe-path>]...
```

- `--mode observe` (default) exposes read-only tools only; `control` (from M6) adds interaction tools and requires `--allow`.
- `--allow` is repeatable. Only windows of allowlisted processes are ever visible to the agent; with no `--allow`, nothing is.
- UAC/logon/credential UI and WinMCP itself can never be targeted.

### Claude Code

This repo's [`.mcp.json`](.mcp.json) registers a published copy in observe mode with only the TestApp allowlisted. It runs from `artifacts/mcp/` rather than `bin/`, so a running client never locks the files that builds and tests overwrite:

```powershell
dotnet publish src\WinMcp.Server -c Release -o artifacts\mcp   # refresh; reconnect the server in your client afterwards
```

Then start Claude Code in the repo root and approve the `winmcp` server when prompted. Or register it yourself:

```powershell
claude mcp add winmcp -- "<repo>\artifacts\mcp\WinMcp.Server.exe" --allow WinMcp.TestApp
```

Then launch `samples\WinMcp.TestApp` and ask, for example: *"Find the WinMCP test application, describe its controls, and tell me the current status."*

After pulling new changes, close sessions that use `winmcp`, re-run the publish command, and reconnect (`/mcp` in Claude Code). A running client keeps the old server process, and Windows won't let the publish overwrite files it has open.

## Layout

| Path | Purpose |
|------|---------|
| `src/WinMcp.Core` | Domain model, errors, policy — no Windows or MCP dependencies |
| `src/WinMcp.Windows` | Win32 / UI Automation implementations |
| `src/WinMcp.Server` | MCP server executable (stdio) |
| `samples/WinMcp.TestApp` | Deterministic WinForms target app for tests |
| `tests/WinMcp.Core.Tests` | Unit tests |
| `tests/WinMcp.Server.Tests` | MCP client ↔ server in-process with a fake desktop |
| `tests/WinMcp.Windows.IntegrationTests` | Real UI Automation against the TestApp |
| `tests/WinMcp.E2ETests` | Spawns the server over stdio and talks MCP to it |
| `spikes/` | Throwaway experiments, not part of the build |
