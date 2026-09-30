# WinMCP

An MCP server that gives AI agents semantic access to native Windows applications — HWND hierarchy, UI Automation tree and Win32 metadata — rather than screenshots and mouse coordinates.

> **Status:** proof of concept — MVP complete (all [MVP criteria](docs/design/mvp-plan.md#mvp-criteria-status--mvp-definition) met, including Claude Code solving the MVP task 5/5). See [docs/design/mvp-plan.md](docs/design/mvp-plan.md), the [tool reference](docs/tools.md), [known limitations](docs/limitations.md) and the [security model](docs/security.md). Tools so far — read-only: `list_windows`, `inspect_window`, `get_ui_tree`, `find_elements`, `inspect_element`, `wait_for`, `capture_screenshot`; with `--mode control` also: `invoke`, `set_value`, `select_option`, `set_toggle`, `send_keys`.

## Requirements

- Windows 10 1809+ / Windows 11
- .NET 10 SDK (pinned in `global.json`)

## Build & test

```powershell
dotnet build WinMcp.slnx
dotnet test --solution WinMcp.slnx                                   # everything (GUI tests open windows)
dotnet test --solution WinMcp.slnx --filter-not-trait "Category=Windows" --ignore-exit-code 8   # no GUI
```

GUI tests (`Category=Windows`) launch `samples/WinMcp.TestApp` (and `charmap.exe`) and need an unlocked, interactive desktop. Don't use the mouse or keyboard while they run; a full run takes about 30 s.

- Launched apps are placed in a Windows job and close automatically when the test process ends, even if a run is interrupted or crashes.
- A run exceeding 5 minutes aborts itself with a message (`WINMCP_TEST_TIMEOUT_MINUTES` to change).
- To see where a run is stuck, capture a hang dump (GUI test projects only; other projects reject the option):
  `dotnet test --project tests\WinMcp.Windows.IntegrationTests --hangdump --hangdump-timeout 2m`

## Running the server

```text
WinMcp.Server [--mode observe|control] [--allow <process-name-or-exe-path>]... [--symbols <process-name>=<path-to-resource.h>]...
```

- `--mode observe` (default) exposes read-only tools only; `control` adds interaction tools and requires `--allow`. Interaction tools refuse disabled elements and password fields, and every action is appended to `%LOCALAPPDATA%\WinMCP\audit\audit-YYYYMMDD.jsonl` (change with `--audit-dir`).
- `--allow` is repeatable. Only windows of allowlisted processes are ever visible to the agent; with no `--allow`, nothing is.
- UAC/logon/credential UI and WinMCP itself can never be targeted.
- `--symbols` maps a Win32/MFC application's control IDs to its `resource.h` names (`1000` → `IDC_EDIT_NAME`) in trees and `inspect_element`, and lets agents find controls by `control_symbol`. The file is re-read when it changes.

### Claude Code

This repo's [`.mcp.json`](.mcp.json) registers a published copy in observe mode with only the TestApp allowlisted. Publish (and re-publish after changes) with:

```powershell
.\scripts\publish-mcp.ps1   # then reconnect the server in your client (/mcp in Claude Code)
```

The script is safe while a client is running the previous build: each publish goes to a new folder under `artifacts\winmcp-builds\`, and the `artifacts\winmcp` junction that `.mcp.json` uses is switched to it. Running servers keep their folder; unused old builds are cleaned up.

Then start Claude Code in the repo root and approve the `winmcp` server when prompted. Or register it yourself:

```powershell
claude mcp add winmcp -- "<repo>\artifacts\winmcp\WinMcp.Server.exe" --allow WinMcp.TestApp
```

Then launch `artifacts\testapp\WinMcp.TestApp.exe` (published by the same script; running it from `bin\` would lock files the build needs) and ask, for example: *"Find the WinMCP test application, describe its controls, and tell me the current status."*

To let the agent interact, change `"observe"` to `"control"` in `.mcp.json` (or pass `--mode control`) and reconnect. Then try the MVP prompt: *"Find the WinMCP test application, inspect its UI, enter 'Mukesh' into the Name field, select HTML from the Type dropdown, click Apply, and tell me the resulting status."*

A running client keeps its server process (and build) until you reconnect; there's no need to stop it before publishing.

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
