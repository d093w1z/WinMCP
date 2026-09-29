# WinMCP

An MCP server that gives AI agents semantic access to native Windows applications — HWND hierarchy, UI Automation tree and Win32 metadata — rather than screenshots and mouse coordinates.

> **Status:** proof of concept, under active design. See [docs/design/mvp-plan.md](docs/design/mvp-plan.md). Tools so far: `list_windows`.

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

This repo's [`.mcp.json`](.mcp.json) registers the Debug build in observe mode with only the TestApp allowlisted. Build first, then start Claude Code in the repo root and approve the `winmcp` server when prompted. Or register it yourself:

```powershell
claude mcp add winmcp -- "<repo>\src\WinMcp.Server\bin\Debug\net10.0-windows10.0.17763.0\WinMcp.Server.exe" --allow WinMcp.TestApp
```

Then launch `samples\WinMcp.TestApp` and ask: *"List the windows WinMCP can see."*

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
