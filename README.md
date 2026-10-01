# WinMCP

An MCP server that gives AI agents semantic access to native Windows applications — window hierarchy, UI Automation tree and Win32 metadata — instead of screenshots and mouse coordinates. An agent finds a window, reads its controls, acts on them by reference (click, type, select, toggle, expand) and checks the result.

**Version 1.0.0.** Works with Win32, WinForms and MFC applications (including MFC Feature Pack and BCGControlBar UIs); other frameworks work as far as their UI Automation support goes. Read-only by default; only the applications you allow are visible.

- [Specification](docs/spec/winmcp-v1.md) · [Tool reference](docs/tools.md) · [Security model](docs/security.md) · [Known limitations](docs/limitations.md) · [MFC investigation](docs/mfc-investigation.md)

## Install

Requirements: Windows 10 (1809 or later) or Windows 11, x64. The release is self-contained — no .NET installation needed.

1. Download `WinMCP-1.0.0-win-x64.zip` from the GitHub release (and optionally check it against `WinMCP-1.0.0-win-x64.zip.sha256`: `Get-FileHash WinMCP-1.0.0-win-x64.zip`).
2. Extract it to a folder of your choice, for example `%LOCALAPPDATA%\Programs\WinMCP`. The archive contains `WinMcp.Server.exe` and the documentation.
3. Register `WinMcp.Server.exe` with your MCP client (below), naming the applications it may access.

## Register with an MCP client

WinMCP is a stdio server: the client starts `WinMcp.Server.exe` with the arguments you configure. Use the full path to the executable; in JSON, backslashes are doubled. The examples allow Notepad in read-only mode.

**Claude Code** (for all your projects):

```powershell
claude mcp add --scope user winmcp -- "C:\Users\you\AppData\Local\Programs\WinMCP\WinMcp.Server.exe" --allow notepad
```

or for one project, in `.mcp.json` at the project root:

```json
{
  "mcpServers": {
    "winmcp": {
      "type": "stdio",
      "command": "C:\\Users\\you\\AppData\\Local\\Programs\\WinMCP\\WinMcp.Server.exe",
      "args": ["--allow", "notepad"]
    }
  }
}
```

**VS Code** (`.vscode/mcp.json`):

```json
{
  "servers": {
    "winmcp": {
      "type": "stdio",
      "command": "C:\\Users\\you\\AppData\\Local\\Programs\\WinMCP\\WinMcp.Server.exe",
      "args": ["--allow", "notepad"]
    }
  }
}
```

**Cursor** (`%USERPROFILE%\.cursor\mcp.json`) and **Claude Desktop** (`%APPDATA%\Claude\claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "winmcp": {
      "command": "C:\\Users\\you\\AppData\\Local\\Programs\\WinMCP\\WinMcp.Server.exe",
      "args": ["--allow", "notepad"]
    }
  }
}
```

Restart or reconnect the server in your client after changing its configuration (in Claude Code: `/mcp`).

## Configure

| Argument | Meaning |
|---|---|
| `--allow <name-or-path>` | Repeatable. An application WinMCP may see: process name without `.exe` (as in Task Manager's *Details* tab, e.g. `notepad`) or the full path of its executable. With no `--allow`, nothing is visible. |
| `--mode observe` | Default. Read-only tools only: list windows, read UI trees, inspect elements, wait for conditions, screenshots. |
| `--mode control` | Adds the interaction tools (`invoke`, `set_value`, `select_option`, `set_toggle`, `set_expanded`, `send_keys`). Requires at least one `--allow`. Every action is logged. |
| `--symbols <name>=<resource.h>` | Repeatable. For Win32/MFC applications you develop: shows your `resource.h` names for control IDs (`#1000 (IDC_EDIT_NAME)`) and lets agents address controls by them. MFC's standard IDs (`AFX_IDW_STATUS_BAR`, `ID_APP_EXIT`, …) are known without it. |
| `--audit-dir <folder>` | Where the action log goes. Default `%LOCALAPPDATA%\WinMCP\audit` (one JSONL file per day). |

A complete example — an agent that may operate one in-house application and read its source names:

```json
"args": ["--mode", "control", "--allow", "MyApp", "--symbols", "MyApp=C:\\src\\MyApp\\resource.h"]
```

To give one application control and another read-only access, register WinMCP twice with different names and arguments.

## Use

Ask the agent in plain language; it chooses the tools. For example:

- *"Find the Notepad window and tell me which menus it has."*
- *"In MyApp, open the Settings dialog, enter 'Mukesh' as the name, select HTML as the type, click Apply and tell me the status."* (needs `--mode control`)
- *"Which dialog resource is MyApp's options window created from, and which controls were added at run time?"*

Good to know:

- Agents address controls by short refs (`e7`) from the UI tree; WinMCP refuses ambiguous targets instead of guessing, refuses disabled controls and password fields, and reports when an application ignored a request.
- Keyboard input (`send_keys`) needs the target window in the foreground; WinMCP brings it there and stops if anything else takes over. A locked desktop blocks it.
- Applications running as administrator can't be read or operated by a normal WinMCP: tools report `ACCESS_DENIED_ELEVATED`.
- Custom-drawn content (canvases, 3D views, some property grids) isn't visible to UI Automation; agents can take a screenshot of it.

See [the tool reference](docs/tools.md) for every tool, result field and error code, and [known limitations](docs/limitations.md) for workarounds.

**Update:** replace the extracted files with the new release and reconnect the server in your client. **Uninstall:** remove the client registration (`claude mcp remove --scope user winmcp`, or delete the JSON entry) and the folder.

## Security in brief

stdio only (no network listener) · read-only unless `--mode control` · only allowlisted applications are ever visible, and nothing about others is revealed · UAC, logon and credential UI can never be targeted · password fields are never read or typed into · screenshots contain only the target window · every action is logged · no code is injected into other processes. Details: [docs/security.md](docs/security.md).

Text displayed by applications is passed to the agent as data. Keep the allowlist narrow and prefer observe mode where interaction isn't needed.

## Build from source

Requirements: .NET 10 SDK (pinned in `global.json`); for the MFC test app, Visual Studio with *Desktop development with C++* and the MFC component.

```powershell
dotnet build WinMcp.slnx
dotnet test --solution WinMcp.slnx                                   # everything (GUI tests open windows)
dotnet test --solution WinMcp.slnx --filter-not-trait "Category=Windows" --ignore-exit-code 8   # no GUI
.\scripts\build-mfc.ps1      # MFC test app (shared and static MFC); its tests are skipped until it is built
.\scripts\package.ps1        # release archive: artifacts\release\WinMCP-<version>-win-x64.zip (+ .sha256)
```

- GUI tests (`Category=Windows`) launch `samples/WinMcp.TestApp`, `samples/WinMcp.MfcTestApp` and `charmap.exe` and need an unlocked, interactive desktop. Don't use the mouse or keyboard while they run (about a minute). Launched apps close automatically when the test process ends; a run over 5 minutes aborts itself (`WINMCP_TEST_TIMEOUT_MINUTES`); `--hangdump --hangdump-timeout 2m` on a GUI test project captures where a run is stuck.
- `WINMCP_SERVER_PATH` points the end-to-end tests at another server executable, e.g. an extracted release package.
- CI (`.github/workflows/ci.yml`) builds and runs all tests, GUI included, on GitHub-hosted Windows runners. Pushing a tag `vX.Y.Z` that matches the version in `Directory.Build.props` builds, tests and publishes the release archive (`release.yml`).

### Development loop with Claude Code

This repository's [`.mcp.json`](.mcp.json) runs a published development build with the test applications allowlisted. `.\scripts\publish-mcp.ps1` publishes the server and the WinForms test app to timestamped folders behind the junctions `artifacts\winmcp` and `artifacts\testapp`, so it is safe while a client still runs the previous build; reconnect (`/mcp`) to switch. Start `artifacts\testapp\WinMcp.TestApp.exe` and try: *"Find the WinMCP test application, inspect its UI, enter 'Mukesh' into the Name field, select HTML from the Type dropdown, click Apply, and tell me the resulting status."*

## Layout

| Path | Purpose |
|------|---------|
| `src/WinMcp.Core` | Domain model, policy, trees, locators, symbols, native-app facts — no Windows or MCP dependencies |
| `src/WinMcp.Windows` | Win32 / UI Automation implementations |
| `src/WinMcp.Server` | MCP server executable (stdio) |
| `samples/WinMcp.TestApp` | Deterministic WinForms target app for tests |
| `samples/WinMcp.MfcTestApp` | The same app in C++/MFC, plus `--frame` (toolbar, status bar, view) and `--features` (Feature Pack controls) modes |
| `tests/WinMcp.Core.Tests` | Unit tests |
| `tests/WinMcp.Server.Tests` | MCP client ↔ server in-process with a fake desktop |
| `tests/WinMcp.Windows.IntegrationTests` | Real UI Automation against the test apps (shared scenarios run on WinForms and MFC) |
| `tests/WinMcp.E2ETests` | Spawns the server over stdio and talks MCP to it |
| `scripts/` | MFC build, packaging, development publishing |
| `docs/` | Specification, tool reference, security, limitations, MFC investigation; `docs/design/mvp-plan.md` is the development history |
| `spikes/` | Research code, not part of the build (M0 UI Automation spike; M11 in-process MFC runtime-class spike) |
