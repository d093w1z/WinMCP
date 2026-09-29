# WinMCP

An MCP server that gives AI agents semantic access to native Windows applications — HWND hierarchy, UI Automation tree and Win32 metadata — rather than screenshots and mouse coordinates.

> **Status:** proof of concept, under active design. See [docs/design/mvp-plan.md](docs/design/mvp-plan.md). No MCP tools are exposed yet.

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

## Layout

| Path | Purpose |
|------|---------|
| `src/WinMcp.Core` | Domain model, errors, policy — no Windows or MCP dependencies |
| `src/WinMcp.Server` | MCP server executable (stdio) |
| `samples/WinMcp.TestApp` | Deterministic WinForms target app for tests |
| `tests/WinMcp.Core.Tests` | Unit tests |
| `tests/WinMcp.Windows.IntegrationTests` | Real UI Automation against the TestApp |
| `tests/WinMcp.E2ETests` | Spawns the server over stdio and talks MCP to it |
| `spikes/` | Throwaway experiments, not part of the build |
