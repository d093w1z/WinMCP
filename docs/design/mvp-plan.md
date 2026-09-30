# WinMCP — MVP Architecture & Development Plan

Status: **Draft for review** · Date: 2026-09-30 · Scope: planning only, no implementation yet

> WinMCP gives AI agents *semantic* eyes and hands for native Windows applications:
> HWND hierarchy + UI Automation tree + Win32 metadata, exposed as a small MCP tool surface.
> Coordinates and screenshots are supporting evidence, not the primary abstraction.

### Decision log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-09-30 | Non-allowlisted windows are **excluded entirely** from all tool output (only `excluded_count` returned) | Titles/contents of unrelated apps may be sensitive or irrelevant. Revisit an opt-in listing mode later. |
| 2026-09-30 | `resource.h` symbol mapping **moved from M11 into MVP (M5)** | Cheap to implement, pure Core logic, high value for developers debugging their own Win32/MFC apps. |
| 2026-09-30 | Handles of non-allowlisted windows return **`WINDOW_NOT_FOUND`**, identical to nonexistent handles (not `TARGET_NOT_ALLOWED`) | Follows from the exclusion decision: a distinct error would confirm the window exists. `TARGET_NOT_ALLOWED` is kept for control actions whose target stops being allowlisted mid-session. |
| 2026-09-30 | Claude Code runs a **published copy** via `.mcp.json`, not `bin/` | A running client locks `bin/` and breaks every build and test run (hit during M3). |
| 2026-09-30 | Publishing uses **timestamped build folders + an `artifacts\winmcp` junction** (`scripts\publish-mcp.ps1`) | A running client also locked the single published folder, blocking updates. Windows refuses to overwrite or rename in-use files/folders, but re-pointing a junction is always allowed; running servers keep their build, unused builds are pruned. Verified by publishing while a server ran from the previous build. |

---

## 0. Decision summary (answers to the 15 architecture questions)

| # | Question | Recommendation | Why (short) |
|---|----------|----------------|-------------|
| 1 | C#/.NET version | **.NET 10 (LTS)**, `net10.0-windows` for Windows projects, `net10.0` for Core | .NET 10 is LTS until Nov 14 2028. **.NET 8 and 9 both reach end of support on Nov 10 2026** — starting on them now means migrating in weeks. .NET 10 is also required for the MCP project templates and `dnx`. |
| 2 | MCP SDK | **`ModelContextProtocol`** (official C# SDK, maintained by the MCP org + Microsoft, v2.2.0 stable, Aug 2026) | Official, stable, stdio transport, attribute-based tools, image results, tool annotations. `ModelContextProtocol.Core` exists if we want fewer deps; `.AspNetCore` only if we ever add HTTP (deferred). |
| 3 | UI Automation API | **UIA3 (COM `IUIAutomation`) via `FlaUI.UIA3` 5.0.0**, hidden behind our own interface | UIA3 is the modern client API; the managed `System.Windows.Automation` (UIA2-era) client is older, slower and misses newer patterns. FlaUI gives pattern helpers + `CacheRequest` support. Its release cadence is slow (last: Feb 2025), so it lives only inside `WinMcp.Windows` and could be swapped for raw COM interop. |
| 4 | Win32 P/Invoke | **`Microsoft.Windows.CsWin32`** (0.3.335, source generator) | Generates exactly the signatures listed in `NativeMethods.txt`; no hand-written, error-prone `DllImport`s. See §B for the list. |
| 5 | Test app: WinForms or WPF | **WinForms** | WinForms controls are real HWND-backed Win32 common controls (`EDIT`, `BUTTON`, `COMBOBOX` under `WindowsForms10.*` classes) — structurally close to MFC. WPF has one HWND per window and pure UIA peers, which would hide exactly the HWND/UIA correlation problems we need to solve. |
| 6 | Robust control identification | Two-level: **session element refs** (`e17`) for agents, **locators** (`automation_id` → `control_id` → `name+type` → path) for durable use; ambiguous matches are errors, never guesses | §D.3 |
| 7 | UIA ↔ HWND correlation | UIA `NativeWindowHandle` (element→HWND; walk ancestors for windowless elements); `ElementFromHandle` (HWND→element); `GetDlgCtrlID` for control IDs | §A.4 |
| 8 | Poor/incomplete UIA trees | Layered fallback: UIA → Win32 HWND enumeration merged into the tree → Win32 messages for actions → screenshot + bounds as last resort. Every node and action reports its **source/method** | §A.5 |
| 9 | Legacy MFC | MVP: standard Win32 controls via UIA's built-in Win32/MSAA proxies **+ `resource.h` symbol mapping** (`1001` → `IDC_EDIT_NAME`, M5). Post-MVP: MFC detection, resource-template parsing. In-process runtime-class inspection is a separate, opt-in research track (C++) | §A.7, §MFC investigation (M11) |
| 10 | Screenshots via MCP | `capture_screenshot` returns an MCP **`image` content block (PNG, base64)** + small JSON metadata; window- or element-scoped; downscaled to a max edge | §C |
| 11 | Unit vs integration tests | Core logic behind interfaces → unit tests with fakes; Server tested in-process via MCP client ↔ server with fake desktop; Windows tests against TestApp, serialized, interactive desktop only | §E |
| 12 | How clients launch it | **stdio**: client spawns `WinMcp.Server.exe` with args; JSON-RPC on stdin/stdout; logs on stderr | §F / README |
| 13 | Packaging | Dev: run built exe. Release 1: self-contained `win-x64`/`win-arm64` zip on GitHub Releases. Release 2: NuGet MCP-server tool package (runnable via `dnx`). Later: winget | §B.4 |
| 14 | Security model | stdio-only (no listener), **process allowlist**, **observe vs control mode** (control tools not even registered in observe mode), per-action target re-validation, audit log, deny-list for secure/system UI, password fields never read | §A.6 |
| 15 | Deferred | Event recording, process launch/kill, diagnostics/ETW/debugger, in-process injection, HTTP transport, coordinate click, AI test generation, non-Win32 frameworks polish | §H |

**C++ is introduced only for:** (a) the MFC test application (M10), and (b) a possible future in-process MFC inspector DLL (research, post-MVP). Nothing in the server needs C++.

---

## A. Architecture

### A.1 Components

```
┌──────────────── MCP client (Claude Code / Cursor / VS Code) ───────────────┐
│  spawns process, speaks JSON-RPC over stdio                               │
└───────────────────────────────┬────────────────────────────────────────────┘
                                │ stdin/stdout (logs → stderr)
┌───────────────────────────────▼────────────────────────────────────────────┐
│ WinMcp.Server  (exe, net10.0-windows)                                      │
│  • Host + DI + config (CLI args / winmcp.json)                             │
│  • Tool classes: thin adapters — validate args, call Core services,        │
│    map results/errors → MCP content (text/structured/image)               │
│  • Registers control tools ONLY in control mode                            │
└───────────────────────────────┬────────────────────────────────────────────┘
                                │ interfaces (IDesktop, IElementProvider, …)
┌───────────────────────────────▼────────────────────────────────────────────┐
│ WinMcp.Core  (net10.0, no Windows deps, fully unit-testable)               │
│  • Data model (Window, Element, TreeNode, ActionResult, WinMcpError)       │
│  • Policy engine (allowlist, mode, deny-list)                              │
│  • Element registry (ref ↔ runtime-id + locator), staleness handling       │
│  • Locator resolution & ambiguity rules                                    │
│  • Tree normalization/pruning/truncation, outline rendering                │
│  • Action strategy selection (pattern → fallback ordering)                 │
│  • Audit log abstraction                                                   │
└───────────────────────────────┬────────────────────────────────────────────┘
                                │ implemented by
┌───────────────────────────────▼────────────────────────────────────────────┐
│ WinMcp.Windows  (net10.0-windows)                                          │
│  • Win32 window enumeration/inspection (CsWin32)                           │
│  • UIA3 access (FlaUI) + CacheRequest-based bulk tree fetch                │
│  • Automation dispatcher: single dedicated MTA worker thread, timeouts     │
│  • Action executors: UIA patterns, Win32 message fallbacks, SendInput      │
│  • Screen capture (PrintWindow / BitBlt) + PNG encode                      │
│  • Process info: path, bitness, integrity level, hung detection            │
└────────────────────────────────────────────────────────────────────────────┘
```

Key rule: **Server knows MCP, not Windows. Windows knows Windows, not MCP. Core knows neither.**

### A.2 Repository layout (revised)

```
WinMCP/
├── WinMcp.slnx                         # .NET projects only
├── Directory.Build.props               # net10.0, nullable, warnings-as-errors, analyzers
├── Directory.Packages.props            # central package versions
├── src/
│   ├── WinMcp.Core/                    # models + policy + registry + normalization (no Windows)
│   ├── WinMcp.Windows/                 # Win32 + UIA + capture implementations
│   └── WinMcp.Server/                  # MCP host exe, tool adapters
├── tests/
│   ├── WinMcp.Core.Tests/              # pure unit tests
│   ├── WinMcp.Server.Tests/            # in-memory MCP client↔server with FakeDesktop
│   ├── WinMcp.Windows.IntegrationTests/# real UIA vs TestApp (Windows, interactive session)
│   └── WinMcp.E2ETests/                # spawns WinMcp.Server.exe over stdio, drives TestApp
├── samples/
│   ├── WinMcp.TestApp/                 # WinForms, deterministic
│   └── WinMcp.MfcTestApp/              # C++/MFC (M10) — separate .sln, needs VS C++ + MFC
├── docs/
│   ├── design/mvp-plan.md              # this file
│   ├── tools.md                        # tool reference with examples
│   ├── limitations.md                  # UIA / elevation / DPI / MFC findings
│   ├── security.md
│   └── mfc-investigation.md            # M11 output
└── README.md
```

Changes from the original proposal:
- **`WinMcp.Models` merged into `WinMcp.Core`.** A separate models assembly adds a project boundary with no consumer that needs it. Split later if a client library appears.
- **`WinMcp.E2ETests` added** so "WinMCP over MCP" failures are distinguishable from "UIA layer" failures.
- **MFC app in its own `.sln`** so `dotnet build` works without the C++/MFC workload installed.

### A.3 Threading & responsiveness

- UIA client calls are cross-process COM calls that **block if the target app is hung**. All UIA/Win32 work runs on a **single dedicated automation thread (MTA)** fed by a queue; tool handlers `await` it with a timeout.
- On timeout → `TARGET_NOT_RESPONDING` (check `IsHungAppWindow`), and the dispatcher abandons and recreates its worker thread so one hung app can't wedge the server.
- Use UIA connection/transaction timeouts (`IUIAutomation2`) — confirm FlaUI exposure in the M0 spike.
- **Bulk-fetch trees with a `CacheRequest`** (one cross-process call for the subtree + selected properties). Naive per-property tree walks are orders of magnitude slower.
- Server process is **Per-Monitor-V2 DPI aware** (app manifest) so UIA bounds, `GetWindowRect`, and capture all agree in physical pixels.

### A.4 HWND ↔ UIA correlation

| Direction | Mechanism |
|-----------|-----------|
| HWND → element | `IUIAutomation.ElementFromHandle(hwnd)` |
| element → HWND | `UIA_NativeWindowHandlePropertyId`; if 0 (windowless element, e.g. list item), walk up to nearest ancestor with a handle and report it as `host_hwnd` |
| HWND → control ID | `GetDlgCtrlID(hwnd)` (only meaningful for child windows) |
| Win32 control → AutomationId | UIA's Win32 proxy generally uses the **control ID** as AutomationId (verify in M10); WinForms uses `Control.Name` |
| Process | `UIA_ProcessIdPropertyId` / `GetWindowThreadProcessId` |

The normalized element carries both worlds: `hwnd`, `host_hwnd`, `control_id`, `class_name`, `automation_id`, `runtime_id`.

### A.5 Degradation strategy for poor UIA trees

1. **UIA (Control view)** — primary.
2. **HWND merge** — enumerate child HWNDs (`EnumChildWindows`) and flag any HWND with no corresponding UIA element as `source: "win32"` nodes (class, control ID, text via `SendMessageTimeout(WM_GETTEXT)`, style bits). This catches custom `CWnd` controls that UIA flattens to an anonymous `Pane`.
3. **Action fallbacks** — Win32 messages for known classes (`BM_CLICK`, `WM_SETTEXT`, `CB_SELECTSTRING` + synthesized `WM_COMMAND/CBN_SELCHANGE`). Every fallback must be verified to trigger the app's notification handlers — e.g. **`CB_SETCURSEL` does *not* send `CBN_SELCHANGE`**, so the app's logic silently won't run.
4. **Focused keyboard input** — `SendInput` after verifying the target is foreground.
5. **Screenshot + bounds** — last resort evidence. Coordinate click is **deferred**; if added later it is a separately gated tool.

Every node reports `source` (`uia`/`win32`) and every action reports the `method` used (`uia.ValuePattern`, `win32.WM_SETTEXT`, …) so agents, tests and humans see how confident the result is.

### A.6 Security model

WinMCP is a local input-injection and screen-reading interface; treat it like one.

- **Transport:** stdio only. No sockets, no HTTP listener in MVP.
- **Modes** (CLI `--mode`):
  - `observe` *(default)*: list/inspect/tree/find/screenshot only. Control tools are **not registered**, so they don't appear in `tools/list`.
  - `control`: adds interaction tools.
- **Allowlist** (required for `control`; also applies to observe): process names and/or full exe paths, e.g. `--allow WinMcp.TestApp`. **Windows from non-allowlisted processes are excluded entirely** from `list_windows` and every other tool — their titles, classes and contents may be sensitive or irrelevant, so the agent never sees them. Only an aggregate `excluded_count` is returned (a number, no details) so the agent can tell "not allowed" from "not running". *(Decision 2026-09-30; an opt-in `allowed:false` listing mode may be revisited later.)*
- **Re-validate on every action:** the element's current process must still be allowlisted (HWNDs and PIDs are reused).
- **Hard deny-list** regardless of config: `consent.exe`, `LogonUI.exe`, `winlogon.exe`, credential UI, secure desktop, the MCP client's own process, WinMCP itself.
- **Password fields** (`IsPassword`): value never returned; `set_value` refused unless `--allow-password-input`.
- **`send_keys`:** target must be allowlisted and verified foreground *immediately before* injection; refuse if foreground changed. System chords (Win+…) blocked.
- **Screenshots:** allowlisted windows only in MVP; no full-desktop capture.
- **Tool annotations:** `readOnlyHint` on observe tools, `destructiveHint: false`/`idempotentHint` set honestly on control tools so clients can prompt appropriately (clients treat annotations as advisory).
- **Audit log:** JSONL of every control action (timestamp, tool, target, method, result) to `%LOCALAPPDATA%\WinMCP\audit\` — also the primary evidence for agent-vs-server failure triage.
- **Symbol files are operator config, not agent input:** `resource.h` paths (§A.7) come only from CLI/config. No tool accepts a file path, so the agent cannot use WinMCP to read arbitrary files.
- **Elevation:** WinMCP runs at medium integrity. UIPI blocks UIA and input to elevated windows; detect via token integrity level and return `ACCESS_DENIED_ELEVATED` rather than failing mysteriously. Running WinMCP elevated is documented but discouraged.

### A.7 Source-aware symbol mapping (`resource.h`)

For developers debugging their own Win32/MFC apps, numeric control IDs are much less useful than the names in their source. WinMCP optionally maps them.

- **Config:** `--symbols <process-name>=<path\to\resource.h>` (repeatable), or a `symbols` section in `winmcp.json`. Operator-supplied only (see §A.6).
- **Parser (Core, pure, unit-tested):** `#define NAME value` lines with decimal or hex values; ignores `_APS_NEXT_*` bookkeeping, comments, and non-numeric macros; tolerates the Visual Studio-generated format and hand-edited variants. Built-in table for standard IDs (`IDOK`=1, `IDCANCEL`=2, `IDABORT`…`IDNO`, `IDC_STATIC`=-1/0xFFFF); common MFC `afxres.h` command IDs (`ID_FILE_OPEN`, …) added in M11.
- **Namespaces:** the same number is routinely reused across kinds (an `IDD_` dialog and an `IDC_` control can both be 1001). Lookup is by *kind*: child controls → `IDC_*` + standard IDs; dialogs → `IDD_*`; menu/toolbar commands → `ID_*`/`IDM_*`; strings → `IDS_*`. Unknown prefixes are only used when nothing else matches.
- **Collisions:** several symbols of the same kind with one value → `control_symbol` is omitted and `control_symbol_candidates` lists them. Never guess.
- **Freshness:** file is re-read when its timestamp changes; a symbol mapped to an ID absent from the live window is simply unused. The mapping is *advisory*: stale headers can mislabel, so `control_id` is always reported alongside.
- **Surfacing:** `inspect_element` → `control_symbol`; tree outline shows `#IDC_EDIT_NAME` when there is no better automation ID; locators accept `control_symbol`; the suggested locator prefers `control_symbol` over a bare `control_id`.

---

## B. Dependencies

### B.1 NuGet packages (verified on nuget.org, 2026-09-30)

| Package | Version | Used in | Purpose |
|---------|---------|---------|---------|
| `ModelContextProtocol` | 2.2.0 (stable) | Server, E2E tests | MCP server hosting, stdio transport, tool attributes; client for tests |
| `Microsoft.Extensions.Hosting` | 10.x | Server | Generic host, DI, config, logging (pulled in transitively) |
| `FlaUI.UIA3` (+ `FlaUI.Core`) | 5.0.0 | Windows | UIA3 COM wrapper, patterns, CacheRequest |
| `Microsoft.Windows.CsWin32` | 0.3.335 | Windows | Source-generated Win32 P/Invoke |
| `System.Drawing.Common` | 10.x | Windows | PNG encoding of captures (Windows-only is fine here). Alternative: WIC via CsWin32 if we want zero extra deps |
| `xunit.v3` | 4.0.1 | tests | Test framework |

Not used: `ModelContextProtocol.AspNetCore` (no HTTP in MVP), Native AOT (FlaUI's COM interop isn't AOT-friendly; not worth fighting).

### B.2 Windows APIs (via CsWin32 `NativeMethods.txt`)

- **Enumeration/hierarchy:** `EnumWindows`, `EnumChildWindows`, `GetParent`, `GetAncestor`, `GetWindow` (`GW_OWNER`), `GetWindowThreadProcessId`
- **Properties:** `GetClassNameW`, `RealGetWindowClassW`, `GetWindowTextW`/`InternalGetWindowText` (top-level captions), `SendMessageTimeoutW` + `WM_GETTEXT` (control text, `SMTO_ABORTIFHUNG`), `GetWindowLongPtrW` (`GWL_STYLE`/`GWL_EXSTYLE`), `GetDlgCtrlID`, `IsWindowVisible`, `IsWindowEnabled`, `IsIconic`, `IsZoomed`, `IsHungAppWindow`
- **Geometry/DPI:** `GetWindowRect`, `DwmGetWindowAttribute` (`DWMWA_EXTENDED_FRAME_BOUNDS`, `DWMWA_CLOAKED`), `GetDpiForWindow`, `MonitorFromWindow`, `GetMonitorInfoW`
- **Process:** `OpenProcess` (`PROCESS_QUERY_LIMITED_INFORMATION`), `QueryFullProcessImageNameW`, `IsWow64Process2`, `OpenProcessToken` + `GetTokenInformation` (`TokenIntegrityLevel`, `TokenElevation`), `EnumProcessModulesEx`/`GetModuleBaseNameW` (MFC detection, M11)
- **Input/focus:** `SetForegroundWindow`, `GetForegroundWindow`, `SendInput`, `PostMessageW`, `SendMessageTimeoutW` (`BM_CLICK`, `WM_SETTEXT`, `CB_*`)
- **Capture:** `PrintWindow` (`PW_RENDERFULLCONTENT`), `GetDC`/`GetWindowDC`, `BitBlt`, `CreateCompatibleDC/Bitmap`
- **Resources (M11):** `LoadLibraryExW` (`LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE`), `FindResourceW`, `LoadResource`, `EnumResourceNamesW`
- **Future (not MVP):** `SetWinEventHook`, UIA event handlers

### B.3 SDKs & tools

- **.NET 10 SDK** — required. Proof-of-concept machine has SDK 10.0.401 / runtime 10.0.12 (incl. WindowsDesktop).
- **Visual Studio 2026** with *.NET desktop development*; add *Desktop development with C++* + *C++ MFC for latest build tools* at M10. CLI-only builds work with just the .NET 10 SDK until M10.
- **Inspection tools (strongly recommended):** Accessibility Insights for Windows, and `Inspect.exe` from the Windows SDK — used to cross-check what WinMCP reports.
- Git 2.53 present.

### B.4 Packaging

| Stage | Form | Client config |
|-------|------|---------------|
| Dev | `dotnet build`, point client at the built `WinMcp.Server.exe` | exe path + args (avoid `dotnet run`: slow startup, build output risk on stdio) |
| Release 1 | Self-contained single-file `win-x64` + `win-arm64` zip on GitHub Releases | exe path + args |
| Release 2 | NuGet tool package with `PackageType=McpServer` + `.mcp/server.json` | `dnx WinMcp.Server --yes -- --mode control --allow …` |
| Later | winget manifest; code-signed binaries | |

x64 server can automate x86 and x64 targets (UIA is out-of-process and bitness-agnostic); bitness only matters for cross-process memory tricks (avoided) and future injection.

---

## C. MCP tool design

### C.1 Tool list (12 tools; 7 observe + 5 control)

| Tool | Mode | Purpose |
|------|------|---------|
| `list_windows` | observe | Top-level windows of allowlisted processes, filterable |
| `inspect_window` | observe | Window + process details, child HWND summary |
| `get_ui_tree` | observe | Pruned UIA tree (outline text + JSON), depth/node limits |
| `find_elements` | observe | Query by locator; returns refs — cheaper than full tree |
| `inspect_element` | observe | Full detail of one element incl. value, patterns, HWND, control ID (this *is* `get_value`) |
| `capture_screenshot` | observe | PNG of a window or element region |
| `wait_for` | observe | Wait until element exists/disappears/has value/state (deterministic sync) |
| `invoke` | control | Click/activate (InvokePattern → Toggle/SelectionItem/ExpandCollapse as appropriate → `BM_CLICK`) |
| `set_value` | control | Set text (ValuePattern → `WM_SETTEXT` → focus+type) |
| `select_option` | control | Select an item in combo/list/tab by name (Expand → SelectionItemPattern.Select → Collapse back to the original state; M0 showed the combo stays expanded otherwise) |
| `set_toggle` | control | Set checkbox to `on`/`off` (idempotent — not a blind toggle) |
| `send_keys` | control | Keyboard input to a verified-foreground allowlisted window |

`get_value` is folded into `inspect_element` (with `fields: ["value"]` for a cheap call). `set_toggle` replaces "toggle" because a target *state* is idempotent and safer for agents that retry.

### C.2 Addressing

Every element-taking tool accepts **either**:
- `"element": "e17"` — a ref returned by `get_ui_tree`/`find_elements` in this session, **or**
- `"window": "hwnd:0x000A0B1C", "locator": { … }` — resolved fresh; must match exactly one element.

Locator fields (all optional, AND-ed): `automation_id`, `control_id`, `control_symbol` (requires a configured `resource.h`), `name`, `name_contains`, `control_type`, `class_name`, `index` (among matches, discouraged).

### C.3 Example calls

**list_windows**
```json
→ {"name":"list_windows","arguments":{"process_name":"WinMcp.TestApp"}}
← {
  "windows":[{
    "hwnd":"hwnd:0x000A0B1C","title":"WinMCP Test App","class_name":"WindowsForms10.Window.8.app.0.2bf8098_r6_ad1",
    "process":{"pid":14820,"name":"WinMcp.TestApp","path":"C:\\…\\WinMcp.TestApp.exe","bitness":64,"elevated":false},
    "visible":true,"enabled":true,"minimized":false,"foreground":false,
    "bounds":{"x":320,"y":180,"width":520,"height":340},"dpi":144,
    "owner":null
  }],
  "excluded_count":37
}
```

**get_ui_tree** (text content the model reads; same data as JSON in `structuredContent`)
```json
→ {"name":"get_ui_tree","arguments":{"window":"hwnd:0x000A0B1C","max_depth":6}}
← text:
[e1] Window "WinMCP Test App" #MainForm
  [e2] Text "Name:"
  [e3] Edit "Name:" #nameTextBox value="" 
  [e4] Text "Type:"
  [e5] ComboBox "Type:" #typeComboBox value="Text" collapsed
  [e6] CheckBox "Enable feature" #enableCheckBox on
  [e7] Button "Apply" #applyButton
  [e8] Button "Cancel" #cancelButton
  [e9] Button "Advanced..." #advancedButton disabled
  [e10] Text "Status: Ready" #statusLabel
(10 nodes, depth 2, truncated=false)
```
The outline is ~5–10× cheaper in tokens than nested JSON. `interactive_only` (default `false`) and automatic collapsing of unnamed single-child `Pane`/`Group` wrappers keep deep MFC/WinForms trees readable. **Decision to validate in M4:** outline-as-text default vs JSON-as-text (spec says SHOULD mirror structuredContent as JSON text; we deviate deliberately for the tree only, if agents perform better).

**find_elements**
```json
→ {"name":"find_elements","arguments":{"window":"hwnd:0x000A0B1C","locator":{"control_type":"Button"}}}
← {"matches":[
    {"ref":"e7","control_type":"Button","name":"Apply","automation_id":"applyButton","enabled":true},
    {"ref":"e8","control_type":"Button","name":"Cancel","automation_id":"cancelButton","enabled":true},
    {"ref":"e9","control_type":"Button","name":"Advanced...","automation_id":"advancedButton","enabled":false}],
   "count":3}
```

**inspect_element**
```json
→ {"name":"inspect_element","arguments":{"element":"e5"}}
← {
  "ref":"e5","control_type":"ComboBox","name":"Type:","automation_id":"typeComboBox",
  "class_name":"WindowsForms10.COMBOBOX.app.0.2bf8098_r6_ad1","framework":"WinForm",
  "hwnd":"hwnd:0x00120B44","control_id":1003,
  "enabled":true,"visible":true,"offscreen":false,"focusable":true,"has_focus":false,
  "value":"Text","options":["Text","HTML","Markdown"],"expand_state":"collapsed",
  "patterns":["Value","ExpandCollapse","Invoke"],
  "bounds":{"x":420,"y":236,"width":180,"height":24},
  "parent":"e1","labeled_by":"e4",
  "locator":{"automation_id":"typeComboBox","control_type":"ComboBox"}
}
```
`locator` is the server's suggested *durable* locator for this element — agents and test generators can reuse it.
`options` must be read **without** opening the dropdown (M0: collapsed combos expose no list items). For Win32/WinForms combos use `CB_GETCOUNT`/`CB_GETLBTEXT` (system-marshalled cross-process; verify in M5); if unavailable, omit `options` rather than expanding as a side effect.

**set_value / select_option / set_toggle / invoke**
```json
→ {"name":"set_value","arguments":{"element":"e3","value":"Mukesh"}}
← {"ok":true,"action":"set_value","element":"e3","method":"uia.ValuePattern","value_after":"Mukesh"}

→ {"name":"select_option","arguments":{"element":"e5","option":"HTML"}}
← {"ok":true,"action":"select_option","element":"e5","method":"uia.SelectionItemPattern","value_after":"HTML"}

→ {"name":"set_toggle","arguments":{"element":"e6","state":"on"}}
← {"ok":true,"action":"set_toggle","element":"e6","method":"none","changed":false,"state_after":"on"}

→ {"name":"invoke","arguments":{"window":"hwnd:0x000A0B1C","locator":{"automation_id":"applyButton"}}}
← {"ok":true,"action":"invoke","element":"e7","method":"uia.InvokePattern"}
```

**wait_for** then read status
```json
→ {"name":"wait_for","arguments":{"element":"e10","condition":"value_contains","text":"Applied","timeout_ms":3000}}
← {"ok":true,"elapsed_ms":42,"element":{"ref":"e10","name":"Status: Applied: Name=Mukesh; Type=HTML; Feature=On"}}
```

**Error (tool execution error, `isError: true`)**
```json
← {"error":{
    "code":"OPTION_NOT_FOUND",
    "message":"ComboBox 'Type:' has no option 'PDF'.",
    "details":{"available":["Text","HTML","Markdown"]},
    "category":"caller","retryable":false,
    "hint":"Use one of the available options."}}
```

**capture_screenshot**
```json
→ {"name":"capture_screenshot","arguments":{"window":"hwnd:0x000A0B1C","max_edge":1280}}
← content: [ {"type":"image","mimeType":"image/png","data":"iVBORw0…"},
             {"type":"text","text":"{\"source_bounds\":{…},\"scale\":0.667,\"method\":\"PrintWindow\"}"} ]
```
Element-scoped capture (`"element":"e5","padding":8`) crops the window capture. Minimized windows → `WINDOW_MINIMIZED` (restoring is a control action, deferred).

### C.4 MCP-level conventions

- Results use `structuredContent` + JSON text (except the tree outline, above) — per spec 2025-11-25.
- Caller/business errors → tool result `isError: true` (model can self-correct). Malformed requests/unknown tools → JSON-RPC protocol errors (SDK default).
- Server `instructions` (sent at initialize) state mode, allowlist, and the recommended workflow: *list_windows → get_ui_tree → act by ref → wait_for/inspect to verify*.

---

## D. Data model (Core)

Wire format is `snake_case` JSON; C# types are records.

### D.1 Application / Process
```
ProcessInfo  { pid, name, path?, architecture? (x86|x64|arm64), elevated? }      (implemented M2; null = couldn't query)
             later: integrity_level, ui_framework_hint
```

### D.2 Window
```
WindowInfo   { hwnd, title, class_name, process: ProcessInfo, visible, cloaked, enabled, minimized, maximized,
               foreground, bounds: Rect (DWM visible frame), dpi, owner? }                 (implemented M2)
             later: is_dialog
WindowDetail : WindowInfo + { style_flags[], ex_style_flags[], child_hwnd_count, top_level_children[],
               uia_root_ref, responding }
```

### D.3 UI element & tree
```
ElementRef   "e<N>"  — session-scoped, stored in ElementRegistry:
             { ref → (window hwnd, UIA runtime_id, durable Locator, last_seen) }
             Stale ref: re-resolve via runtime_id; else via locator (only if unique);
             else ELEMENT_STALE. Refs never silently rebind to a different element.

ElementSummary { ref, control_type, name, automation_id?, value?, states[] (disabled, offscreen,
                 collapsed/expanded, on/off/indeterminate, selected, focused, password), source }
ElementDetail  : ElementSummary + { class_name, framework, hwnd?, host_hwnd, control_id?,
                 control_symbol?, control_symbol_candidates[]?, runtime_id,
                 enabled, visible, offscreen, focusable, has_focus, patterns[], options[]?,
                 bounds, parent, labeled_by?, help_text?, locator }
TreeNode       : ElementSummary + { children[] }
UiTree         { window, root: TreeNode, node_count, max_depth_reached, truncated, truncation_reason? }
Locator        { automation_id?, control_id?, control_symbol?, name?, name_contains?, control_type?, class_name?, index? }
```

Identification priority for the suggested locator: stable `automation_id` (not numeric-looking-dynamic in WinForms) → `control_symbol` (when `resource.h` configured and unambiguous) → `control_id` (Win32/MFC, unique within parent dialog) → `name + control_type` (localization-sensitive) → `labeled_by` text → structural path (last resort, flagged `fragile: true`).

### D.4 Interaction result
```
ActionResult { ok, action, element (ref), method, changed?, value_after? / state_after?, warnings[], elapsed_ms }
```

### D.5 Error
```
WinMcpError { code, message, category, retryable, hint?, details? }
category ∈ caller      — agent/caller mistake: INVALID_ARGUMENT, WINDOW_NOT_FOUND, ELEMENT_NOT_FOUND,
                          AMBIGUOUS_MATCH, OPTION_NOT_FOUND, PATTERN_NOT_SUPPORTED, ELEMENT_DISABLED, ELEMENT_STALE
           policy      — TARGET_NOT_ALLOWED, OPERATION_NOT_PERMITTED, PASSWORD_FIELD
           environment — ACCESS_DENIED_ELEVATED, TARGET_NOT_RESPONDING, WINDOW_MINIMIZED,
                          WINDOW_CLOSED, FOCUS_FAILED, TIMEOUT
           internal    — INTERNAL_ERROR (a WinMCP bug; always logged with stack to stderr)
```
`retryable` means *the same call, unchanged, may succeed later* — only `TARGET_NOT_RESPONDING`, `FOCUS_FAILED` and `TIMEOUT`. Caller errors are never retryable as-is; the arguments must change. Category and retryability are derived from the code, never set independently (implemented in M1: `WinMcp.Core/Errors`).
`category` is the main tool for "was it the agent or WinMCP?" triage: any `internal` error in an E2E run is a WinMCP bug by definition.

---

## E. Testing plan

### E.1 Test application (WinForms, `WinMcp.TestApp`)

v1 (built in M1, used by MVP; contract pinned by `TestAppContractTests`):
```
Name:            [________________]        #nameTextBox
Type:            [ Text ▼ ]                #typeComboBox (DropDownList: Text, HTML, Markdown)
Enable feature:  [x]                       #enableCheckBox (default on)
                 [ Apply ] [ Cancel ] [ Advanced... ]  #applyButton #cancelButton #advancedButton (disabled)
                 [ Slow apply ] [ Add field ]          #slowApplyButton #addFieldButton
Dynamic 1:       [________________]        #dynamicTextBox1..3 (only after Add field)
Status: Ready                              #statusLabel
Events: 0 []                               #eventLogLabel
(hidden) #hiddenTextBox (Visible=false)
```
Deterministic behavior:
- **Apply** → `Status: Applied: Name=<name>; Type=<type>; Feature=<On|Off>`
- **Cancel** → restores all defaults, **removes dynamic fields, clears the event log**, `Status: Ready` — a full in-process reset so tests needn't relaunch
- **Slow apply** → `Status: Applying...` immediately, then Apply after a fixed 1500 ms (tests `wait_for`)
- **Add field** → creates `Dynamic N:` + `#dynamicTextBoxN`, max 3
- **Advanced...** enabled only when *Enable feature* is on *and* Name is non-empty; click → `Status: Advanced options opened`
- **Event log** format `Events: <count> [<control>.<event>, ...]`, e.g. `nameTextBox.TextChanged`, `typeComboBox.SelectedIndexChanged`, `typeComboBox.DropDown`, `typeComboBox.DropDownClosed`, `enableCheckBox.CheckedChanged`, `addFieldButton.FieldAdded`, `dynamicTextBox1.TextChanged`
- Captions use ASCII `...`, not `…`, so agents and tests can type them
- `--position x,y` for stable placement; fixed font (Segoe UI 9pt); PerMonitorV2 via `ApplicationHighDpiMode`; no animations or timers besides Slow apply.
- The **Events** log proves an action went through the app's real event handlers, not just a visual change. Tests assert *which* events occurred, not exact counts (M0: `ValuePattern.SetValue` raises `TextChanged` twice).

v2 (M9, post-MVP): MenuStrip + context menu, ListView (details, 5 rows), TreeView (3 levels), modal dialog (`OK/Cancel`), tab control, a custom-painted control with no accessibility (to test degradation).

### E.2 Unit tests (no GUI)
- Locator matching & ambiguity rules; suggested-locator generation.
- Tree pruning/collapsing, depth/node truncation, outline rendering (golden files).
- Element registry: ref issuance, stale detection, re-resolution never rebinds to a different element.
- Policy: allowlist matching (name/path/case), deny-list, mode gating, password handling.
- Error mapping (exception → `WinMcpError` category/code).
- `resource.h` parser & lookup: decimal/hex values, `_APS_NEXT_*` ignored, standard IDs, kind-based namespaces (`IDD_` vs `IDC_` sharing 1001), collisions → candidates, malformed lines skipped, reload on timestamp change (golden sample headers incl. a real VS-generated one).
- Policy: `list_windows` output contains no field derived from non-allowlisted windows beyond `excluded_count`.
- Server.Tests: MCP client ↔ server in-process against `FakeDesktop`: `tools/list` differs by mode, argument validation → `isError`, structured output shape, image content block for screenshots.

### E.3 Windows integration tests (real UIA vs TestApp)
Run serialized (one collection, no parallelization), require an interactive desktop, fixture launches a fresh TestApp per test class (and resets state via Cancel per test). Tag `[Trait("Category","Windows")]`.

1. `list_windows` finds TestApp by process name; PID equals the launched process.
2. Non-allowlisted process (fixture-launched second app) is excluded, `excluded_count ≥ 1`.
3. `inspect_window` reports class, bitness, `elevated=false`, bounds within a monitor, `responding=true`.
4. `get_ui_tree` contains all v1 controls with expected control types and AutomationIds.
5. `max_depth=1` / `max_nodes=3` → `truncated=true` with reason.
6. `find_elements` by `automation_id` returns exactly one; by `control_type=Button` returns 5.
7. Interaction via ambiguous locator (`control_type=Button`) → `AMBIGUOUS_MATCH` with candidates; app state unchanged.
8. `inspect_element(nameTextBox)` → non-zero `hwnd`, `control_id`, `Value` in patterns, `labeled_by` resolves to "Name:".
9. `set_value("Mukesh")` → `value_after == "Mukesh"` **and** event log contains `TextChanged`.
10. `select_option("HTML")` → value HTML, event log contains `SelectedIndexChanged`, combo ends `collapsed`.
11. `select_option("PDF")` → `OPTION_NOT_FOUND` listing the 3 options.
12. `set_toggle(off)` then `set_toggle(off)` again → second call `changed=false`.
13. **Golden scenario:** set Name, select HTML, ensure feature on, invoke Apply → Status exactly `Status: Applied: Name=Mukesh; Type=HTML; Feature=On`.
14. `invoke(advancedButton)` while disabled → `ELEMENT_DISABLED` **and the app's Click handler did not run** (M0: raw `InvokePattern.Invoke` on a disabled WinForms button *does* run it); enable via checkbox + name → invoke succeeds.
15. Hidden control is not in the tree; locator for it → `ELEMENT_NOT_FOUND` (document exact UIA behavior found).
16. `invoke(addFieldButton)` → `wait_for(exists, dynamicTextBox1)` succeeds → `set_value` on it works.
17. `invoke(slowApplyButton)` → `wait_for(value_contains "Applied", 3000)` succeeds; with `timeout_ms=200` → `TIMEOUT`.
18. Stale ref: obtain ref for dynamic field, recreate it, reuse old ref → `ELEMENT_STALE` or correct re-resolution (never wrong element).
19. `capture_screenshot(window)` → valid PNG, dimensions ≈ bounds × scale, non-uniform pixels; element capture ≈ element bounds.
20. `send_keys` into Name while Notepad is foreground → either brings TestApp to foreground and types correctly, or `FOCUS_FAILED` — **never** types into Notepad (assert Notepad text unchanged).

Flakiness controls: no `Thread.Sleep` in tests (use `wait_for`/polling with deadlines), fresh process per class, fixed window position, test run aborts early if session is locked/non-interactive, retry-free (a flake is a bug to fix, not to retry).

### E.4 End-to-end MCP tests
`WinMcp.E2ETests` uses the SDK's MCP **client** with a stdio transport to spawn the real `WinMcp.Server.exe --mode control --allow WinMcp.TestApp`, then runs the golden scenario and 3–4 negative cases purely through `tools/call`. Asserts: results, no `internal` errors, and that audit-log `method` values are all semantic (`uia.*`/`win32.*`, never coordinates).

**Agent evals (manual → scripted, not in CI):** run the natural-language prompt through Claude Code N times; record tool-call transcripts + audit log. Triage rule: *if E2E passes but the agent fails, it's a tool-design/description problem or agent error* — read the transcript; `caller`-category errors show where the agent went wrong and inform description/tool-shape improvements.

### E.5 CI
- GitHub Actions `windows-latest`: build + unit + Server tests always. Try Windows integration/E2E tests on hosted runners early (M3); if the desktop session proves unreliable, move them to a self-hosted runner and keep them mandatory locally.

---

## F. Milestones (each ends with something runnable)

| M | Deliverable | Runnable proof |
|---|-------------|----------------|
| **M0 — Spike (1–2 days)** | Throwaway console app: FlaUI on .NET 10 dumps the UIA tree of Notepad and a scratch WinForms form with CacheRequest; measure time; confirm timeouts API, DPI awareness, `NativeWindowHandle`/`GetDlgCtrlID` values | Console prints tree + timings. Findings appended to this doc. |
| **M1 — Skeleton + TestApp v1** ✅ | `WinMcp.slnx`, `global.json` (SDK + Microsoft Testing Platform), Directory.Build/Packages.props, Core (error model), Server (stdio host, no tools), TestApp v1, Core.Tests, Windows.IntegrationTests (TestApp contract via raw FlaUI), E2ETests (stdio handshake), CI workflow | 54 tests green; GUI tests 5/5 repeat runs green |
| **M2 — MCP server + `list_windows`** ✅ | `WinMcp.Windows` (`Win32Desktop` via CsWin32), `WinMcp.Server.Tests` (real MCP client ↔ server over in-memory pipes, FakeDesktop). `--mode`/`--allow` parsing, `TargetPolicy` (allowlist + hard deny-list), `WindowQuery`, `list_windows`, structured error filter, server instructions, PMv2 manifest, `.mcp.json` for Claude Code | 114 tests green (E2E: TestApp discovered through MCP over stdio) |
| **M3 — `inspect_window` + integration harness** ✅ | `inspect_window` (responding, decoded styles/ex-styles, owned windows, child-window class summary, framework hint); `TestAppSession` as a class fixture with `Reset()` | 147 tests green; GUI suite 3/3 repeat runs |
| **M4 — `get_ui_tree` + `find_elements` + refs** ✅ | `AutomationDispatcher` (dedicated MTA thread, 3 s UIA timeouts, 10 s hard timeout with thread abandonment), `UiaAutomation` (single cached fetch), `TreeNormalizer`, `ElementRegistry`, `UiTreeService`, `OutlineRenderer`; TestApp Freeze button and `--stress N` | 207 tests green; hung-target path and 634-node benchmark measured |
| **M5 — `inspect_element` + symbol mapping** ✅ | `inspect_element` (by ref or hwnd + unique locator; `AMBIGUOUS_MATCH` lists candidate refs), live extras (framework, patterns, focusability, help text, labeled_by, control ID), combo options via `CB_GETLBTEXT` + `option_count`, suggested locator checked for uniqueness; `resource.h` parser/`SymbolTable`/`SymbolProvider`, `--symbols`, `control_symbol` in outline/find/inspect. Test infrastructure: job object for launched apps, run watchdog, hang dumps | 248 tests green; symbols verified live against charmap (a real `#32770` dialog) ahead of the MFC app |
| **M6 — Interaction + `wait_for`** ✅ | `invoke`, `set_value`, `select_option`, `set_toggle` (control mode only), `wait_for` (both modes), `InteractionService` (enabled/password/mode checks, audit), `UiaActions` (patterns + Win32 fallbacks), JSONL audit log, `--audit-dir` | 290 tests green; **golden scenario passes in integration and in E2E over stdio**, audit shows only semantic methods; GUI suites 3/3 runs |
| **M7 — Screenshots** ✅ | `capture_screenshot` (window or element + padding, `max_edge` downscaling, PNG image block + JSON metadata), `PrintWindowCapture` (PW_RENDERFULLCONTENT, hang-safe), `ELEMENT_OFFSCREEN` | 312 tests green; captures inspected visually; covered-window capture shows no pixels of the window on top |
| **M8 — `send_keys`, E2E, hardening → MVP** ✅ | `send_keys` with per-chunk foreground verification, 3× faster E2E, docs, DPI fixes (library + TestApp), 20/20 stability at 150%, agent eval 5/5 | **All 8 MVP criteria met — MVP complete (2026-09-30)** |
| M9 — TestApp v2 breadth | Menus, ListView, TreeView, dialog, tabs, custom-painted control; tree/actions extended (ExpandCollapse, grid/table items, menu navigation). **Budgeted tree fetch** (walk level by level, stop at max_nodes/max_depth; target-side search for refs and find_elements) so large lists don't hit the UIA timeout — see M4 notes | New integration tests; 10k-row list stays under the timeout |
| M10 — MFC test app | C++/MFC dialog app equivalent to v1+v2 (CDialog, CEdit, CComboBox, CButton, CListCtrl, CTreeCtrl) + a CFrameWnd/CView doc app with menu/toolbar/status bar. **Same integration suite parameterized over both apps** (per-app locator map — MFC map uses `control_symbol` locators via the app's own `resource.h`) | Suite runs against MFC app; symbol mapping verified live; diffs documented |
| M11 — MFC investigation | See below; `docs/mfc-investigation.md`; go/no-go on in-process inspector | Report + enrichment features that proved reliable |

Differences from the original sequence: a spike (M0) de-risks FlaUI/.NET 10/perf before committing; the test app is built **first**; integration tests grow with each milestone instead of arriving at M8; `wait_for` joins interaction (without it, tests and agents race the UI).

### M11 — MFC investigation plan (what to try, in order of cost)

Out-of-process, no injection (likely reliable):
1. **Detect MFC:** loaded modules `mfc140u.dll`/`mfc140.dll` (shared MFC); for static MFC, heuristics: `Afx:`-prefixed or `AfxWnd…`/`AfxFrameOrView…` window classes. Report `framework: "MFC (shared, v14.x)"` or `"MFC (static, heuristic)"`.
2. **Map HWND → role:** `#32770` = dialog (CDialog), `Afx:*` frame/view (CFrameWnd/CView), standard control classes → CButton/CEdit/CComboBox/CListCtrl (`SysListView32`)/CTreeCtrl (`SysTreeView32`)/CStatusBar (`msctls_statusbar32`)/CToolBar (`ToolbarWindow32`). This is the *likely* MFC wrapper class, labelled as inferred.
3. **Dialog resource templates:** load the exe as a data file, parse `RT_DIALOG`, match live dialogs by control-ID set → design-time captions, styles, control types, and **dialog resource ID**; `RT_MENU`/`RT_STRING` for menu command IDs and strings.
4. **Symbolic IDs:** already implemented in M5 (§A.7). In M11: add the MFC `afxres.h` command-ID table, combine with dialog templates (`IDD_*` for live dialogs matched in step 3), and measure accuracy on the MFC test app and one real-world MFC app.
5. **Measure UIA quality** on each MFC control type: names from label heuristics (static-before-control ordering), owner-drawn buttons, CListCtrl items/subitems, CTreeCtrl expand/collapse, CView content (typically an opaque Pane), MFC Feature Pack controls if used.

In-process (research only, opt-in, C++, separate component):
6. **Runtime class via injected helper:** `CWnd::FromHandlePermanent` → `GetRuntimeClass()->m_lpszClassName`. Hard constraints to document: MFC handle maps are **per-thread**, so the code must run on the window's UI thread (e.g. `SetWindowsHookEx(WH_CALLWNDPROC, threadId)` + registered message); it only works when the target uses the **same shared MFC DLL** (a statically linked MFC has a private handle map the helper can't reach); needs **matching bitness** (x86 and x64 builds); blocked by UIPI for elevated targets; injection is invasive and may trip EDR/AV. Expected verdict: useful for "debug my own app" scenarios, not a general capability.

---

## G. Risks

| Risk | Impact | Mitigation |
|------|--------|-----------|
| **UIA gaps in legacy apps** (owner-drawn, custom `CWnd` painting, unnamed controls) | Elements missing/anonymous | HWND merge (§A.5), control-ID locators, resource-template names, screenshots as evidence; document per control type |
| **Wrong label heuristics** (UIA derives an edit's name from the preceding static) | Agent picks the wrong field | Prefer automation_id/control_id locators; expose `labeled_by`; tests assert names |
| **MFC runtime class unavailable out-of-process** | Less rich than hoped | Inferred class + resources + resource.h; in-process only as research |
| **Elevated targets / UIPI** | UIA and input silently fail | Detect integrity level; `ACCESS_DENIED_ELEVATED`; document "run WinMCP elevated" trade-off; uiAccess not planned |
| **Hung target app** | UIA calls block | Dedicated worker thread + timeouts + `IsHungAppWindow`; recreate worker |
| **DPI / scaling / mixed-DPI multi-monitor** | Bounds and captures misaligned | PMv2-aware server; physical pixels everywhere; report DPI per window; test at 100% and 150% |
| **Multiple monitors** (negative coords) | Crop/bounds errors | Virtual-screen coordinate space; tests with window on secondary monitor (manual) |
| **32 vs 64-bit** | Minor for UIA; major for message tricks/injection | Avoid cross-process struct messages (`LVM_GETITEMTEXT` etc.) — use UIA; injection deferred |
| **Focus / foreground lock** | `SetForegroundWindow` refused; keys go elsewhere | Prefer patterns (no focus needed); `send_keys` verifies foreground, fails closed |
| **Fallback messages bypass app logic** (e.g. `CB_SETCURSEL` without `CBN_SELCHANGE`) | UI looks right, app state wrong | Synthesize notifications where documented; Events-counter tests; report `method` |
| **Flaky GUI tests** | Lost trust | Serialized, fresh app, no sleeps, deterministic app, zero-retry policy, run-count gate in MVP |
| **Custom-rendered UIs** (games, DirectX, some Qt/Electron without a11y) | Opaque | Out of scope for MVP; screenshot only; documented |
| **FlaUI maintenance cadence** | Blocked on a bug | Confined to `WinMcp.Windows`; small surface; can drop to raw UIA COM |
| **Token cost of trees** | Agents lose context | Outline format, pruning, `find_elements`, node caps |
| **Security misuse** (agent drives the wrong app, prompt injection via UI text) | Real damage | Allowlist, observe default, re-validation, deny-list, audit, UI text is data not instructions (stated in server instructions) |
| **Clean-session/CI availability of interactive desktop** | Integration tests can't run in CI | Verify at M3; self-hosted runner fallback |

---

## H. Deferred (explicitly not in MVP)

Event/message recording (`WM_COMMAND`, `WM_NOTIFY`, WinEvents, UIA events); `launch/terminate/restart_application`; diagnostics (logs, crash detection, Event Log, ETW, debugger, modules/threads); in-process injection; coordinate-based click/drag; HTTP/remote transport; full-desktop screenshots; menu/ListView/TreeView depth (M9, post-MVP); WPF/WinUI/Qt/Electron-specific work; AI-driven exploration/test generation; code signing/winget.

Architectural compatibility is preserved: events fit as a new `IEventSource` in Windows + tools in Server; process management as a separately gated `admin` mode; diagnostics as a separate tool class.

---

## MVP definition

**WinMCP MVP is complete when all of the following are true:**

1. **Build & unit/server tests:** from a clean clone on Windows 11 x64 with only the .NET 10 SDK, `dotnet build` succeeds and `WinMcp.Core.Tests` + `WinMcp.Server.Tests` pass.
2. **Integration reliability:** the 20 integration tests in §E.3 pass **20 consecutive runs with zero failures** on the development machine.
3. **Protocol-level E2E:** `WinMcp.E2ETests` spawns the real server over stdio and completes the golden scenario (Name=`Mukesh`, Type=`HTML`, feature on, Apply) with Status exactly `Status: Applied: Name=Mukesh; Type=HTML; Feature=On`, and the audit log shows **only semantic methods** (`uia.*` / `win32.*`), no coordinate input.
4. **Agent demo:** Claude Code, configured via one documented command, given only the prompt *"Find the WinMCP test application, inspect its UI, enter 'Mukesh' into the Name field, select HTML from the Type dropdown, click Apply, and tell me the resulting status."* reports the exact status string in **≥ 4 of 5** fresh sessions, without calling `capture_screenshot`.
5. **Tool surface:** exactly the 12 tools in §C.1; in default `observe` mode `tools/list` contains only the 7 observe tools.
6. **Security defaults verified by tests:** control mode requires an allowlist; actions on a non-allowlisted window return `TARGET_NOT_ALLOWED`; `send_keys` never types into a non-target window.
7. **Performance:** `get_ui_tree` on TestApp < 500 ms and each interaction < 300 ms (median over 20 runs, excluding `wait_for`).
8. **Docs:** README quickstart (build, register in Claude Code and one of Cursor/VS Code), `tools.md` with examples, `limitations.md` and `security.md` reflecting what was actually observed.

---

## M0 spike findings (2026-09-30)

Spike: [`spikes/M0.UiaSpike`](../../spikes/M0.UiaSpike) (throwaway; raw output in `results.md`). Targets: a WinForms form mirroring TestApp v1 (+ a "Hang 8 s" button) and `charmap.exe` as a classic Win32 `#32770` dialog. Notepad was deliberately skipped: on Windows 11 it is WinUI (not classic Win32) and restores previous tabs, so dumping it could read the user's documents.

Environment: .NET 10.0.12, Windows 11 26100, x64, single monitor at 96 DPI (100%).

| # | Question | Result | Consequence for the design |
|---|----------|--------|----------------------------|
| 1 | FlaUI.UIA3 5.0.0 + CsWin32 on `net10.0-windows` | Works. Main thread MTA, Per-Monitor-V2 aware via manifest | Stack confirmed. Set `TargetPlatformMinVersion` 10.0.17763 to silence CA1416 on DPI APIs. For WinForms apps use `ApplicationHighDpiMode` instead of a manifest (WFO0003). |
| 2 | UIA timeouts | `UIA3Automation.TransactionTimeout`/`ConnectionTimeout` are settable `TimeSpan`s. With defaults, a read against a hung app **blocked for the full 7.5 s hang**; with 2 s it failed after 2.0 s with `COMException 0x80131505` (UIA_E_TIMEOUT). `IsHungAppWindow` was `true` ~6 s into a hang | Set TransactionTimeout (≈3 s) at startup; map `0x80131505` → `TARGET_NOT_RESPONDING`, and use `IsHungAppWindow` for the hint. The worker-thread design in §A.3 stays as a backstop. |
| 3 | Win32 AutomationId vs control ID | **13/13 HWND-backed controls in charmap: `AutomationId == GetDlgCtrlID`** | Assumption in §A.4 confirmed. `control_symbol` (§A.7) can be resolved from AutomationId alone for Win32/MFC controls. |
| 4 | WinForms identity | AutomationId = `Control.Name`, class `WindowsForms10.<Class>.app.0.<hash>` (hash varies between builds) | Never put WinForms class names in durable locators. |
| 5 | Label heuristic | Edits/combos take their **Name from the preceding label** ("Name:", "Characters to copy :"); `LabeledBy` was **empty** for WinForms | Don't rely on `labeled_by`; the name already carries the label. Emit `labeled_by` only when UIA provides it. |
| 6 | Collapsed combo | WinForms DropDownList combo: patterns `ExpandCollapse, Invoke, Value` (**no Selection**); **zero list items while collapsed**; after `SelectionItem.Select` it **stays expanded** | `select_option` = expand → select → collapse. Read `options` via `CB_GETLBTEXT` without expanding (§C.3). |
| 7 | App event side effects | `SetValue("Mukesh")` fired `TextChanged` ×2; select fired `DropDown, SelectedIndexChanged, DropDownClosed` | Tests assert events by name, not count; document side effects in tools.md. |
| 8 | **Disabled controls** | `IsEnabled=false` but Invoke still reported supported, and **`Invoke()` ran the button's Click handler** | **WinMCP must check `IsEnabled` before every action** (`ELEMENT_DISABLED`). This is correctness/safety, not cosmetics. Integration test 14 asserts the handler did not run. |
| 9 | Hidden controls | `Visible=false` textbox absent from the control view | Matches test 15's expectation. |
| 10 | Custom-drawn control | charmap's `CharGridWClass` → one `Pane "Character Grid"`, no children | Confirms the degradation case in §A.5; a realistic stand-in for custom MFC `CWnd`s. |
| 11 | Tree noise | Control view includes the `TitleBar` subtree (System menu, Min/Max/Close) and a combo's inner `Text` + `Open` button | Normalization: prune `TitleBar` by default and fold combo internals into the combo node. |
| 12 | Bounds | UIA `BoundingRectangle` == `GetWindowRect` (includes invisible resize borders); DWM extended frame is 7 px narrower on each side (the visible window) | Window `bounds` and screenshot crops use `DWMWA_EXTENDED_FRAME_BOUNDS`; element bounds use UIA. |
| 13 | Performance | Trees of 20–27 nodes: 30–170 ms. Caching sped up one run ×5 and slowed another (×0.6), so it's **inconclusive on small trees**. Each interaction 4–12 ms; whole scenario 150–180 ms | MVP performance targets look easy. Benchmark caching on a 500+ node tree in M4 before committing to it as the default. |
| 14 | Status after Invoke | Status label updated within 1–2 ms of `Invoke()` | `wait_for` is still needed for async apps (Slow apply) but not for the simple path. |

Open items: **mixed/high-DPI behaviour is untested** (the machine is at 100%); rerun the spike at 150% scaling before M7 (screenshots). Hung-app detection timing (~5 s for `IsHungAppWindow`) measured once only.

---

## M2 implementation notes (2026-09-30)

- **Policy before filters.** `list_windows` applies the allowlist first, then the caller's filters, and counts `excluded_count` before filtering, so a filter like `title_contains: "password"` can't reveal anything about non-allowlisted windows (unit- and server-tested). The hint on an empty result names the allowlist instead.
- **Deny-list:** `consent`, `LogonUI`, `winlogon`, `CredentialUIBroker`, plus WinMCP's own PID. Deny wins over allow.
- **`--mode` accepts only `observe`/`control`.** `Enum.TryParse` would have accepted `--mode 1` as control; the parser matches names explicitly and a test pins it.
- **Parameter names are snake_case at the C# level** (`process_name`); the SDK uses them verbatim in the input schema. Output uses a snake_case `JsonSerializerOptions`, which **must set `TypeInfoResolver`**: the SDK makes the options read-only, which throws without one (caught by Server.Tests before any client saw it).
- **Error mapping:** a call-tool filter sees tool exceptions. `WinMcpException` → `isError` + `{ "error": { code, message, category, retryable, hint?, details? } }` in both text and `structuredContent`; any other exception → `INTERNAL_ERROR` with the details logged to stderr only.
- **Hung apps:** enumeration uses only calls that don't send messages to the target (`GetWindowText` reads the cached caption cross-process), so a hung app can't block `list_windows`.
- **Startup:** ~450 ms to the first MCP response; exits ~40 ms after stdin closes.
- Plan §E.3 tests 1–3 are covered at the Win32 layer, and test 1 also end-to-end through MCP.

## M3 implementation notes (2026-09-30)

- **`inspect_window` accepts top-level windows only**; a control's handle gets `INVALID_ARGUMENT` pointing to top-level windows (controls are `inspect_element`'s job, M5).
- **`owned_windows`** lists shown, allowlisted top-level windows whose owner is this window — the cheap, reliable way for an agent to notice a modal dialog before M9 adds dialog support.
- **`child_windows`** = total descendant HWND count + the 15 most common classes. Class names are dictionary *keys*; the snake_case policy doesn't touch them (tested), so `WindowsForms10.BUTTON.app.0.…` arrives verbatim.
- **`framework_hint`** is class-name heuristics only (WinForms, WPF, MFC, WinUI 3, UWP, Chromium, Qt, Java AWT, plain `#32770` dialog). Loaded-module detection (e.g. `mfc140u.dll`) stays in M11.
- **Styles** are decoded to WinUser.h names, with `WS_CAPTION` in place of `WS_BORDER|WS_DLGFRAME` and unknown bits reported as hex rather than dropped.
- Test harness: one TestApp per test class, reset via Cancel before each test — the GUI suite doubled to 12 tests with no increase in run time (~7.5 s).
- Not in M3 despite the original row: process details and elevation (already delivered in M2). Hung-window behaviour (`responding: false`) is untested until a hang control is added to the TestApp alongside UIA timeouts (M4).

## M4 implementation notes (2026-09-30)

- **Refs** (`e1`, `e2`, …) are keyed by (window, UIA runtime id), stable for the server's lifetime, shared by `get_ui_tree` and `find_elements`, and assigned in reading order (window first). A ref whose element disappeared → `ELEMENT_STALE`; an unknown ref → `ELEMENT_NOT_FOUND`. Refs never rebind. The registry is unbounded per session (fine for an MVP; revisit for long-lived servers).
- **`element` alone is enough** to scope `get_ui_tree`/`find_elements`; the window is re-validated through the same allowlist gate every time, so a ref can't outlive the policy.
- **Normalization:** title bar subtree removed; a ComboBox's inner `Text` and `Button` removed (kept: an expanded combo's `List`/`ListItem`s); unnamed, id-less single-child `Pane`/`Group`/`Custom` wrappers collapsed. `find_elements` searches the normalized tree, so it never returns removed noise.
- **Outline is the text content, JSON the structured content** of `get_ui_tree` (with a declared output schema). Values over 200 chars are cut; password values are never returned (`password` state instead).
- **`find_elements` validates `control_type`** against UIA names before touching the target, with corrections for common mistakes (`TextBox` → `Edit`, `Label` → `Text`, …).
- **Hung target (TestApp "Freeze 8s"):** `TARGET_NOT_RESPONDING` after ~3 s; `inspect_window` shows `responding: false` from ~5 s; everything works again after the freeze. Hard-timeout abandonment verified with synthetic work (0.5 s → fresh thread serves the next call).
- **Large trees — measured, and a known limitation.** 634-node WinForms tree: naive walk ~2.0 s, cached fetch ~1.5 s, and a cache request with *no properties at all* ~1.6 s. The cost is the target's own tree traversal (~2.5 ms/element for WinForms, ~1.3 ms for native Win32 in M0), not what WinMCP asks for, so cache tuning can't fix it. Consequence: a window with thousands of elements could exceed the 3 s UIA timeout while healthy. Mitigation now: if the window is responding, the error hint says the tree is probably too large instead of implying a hang. Fix planned for M9: budgeted, level-by-level fetch that stops at `max_nodes`/`max_depth`.
- TestApp tree: ~100–150 ms per `get_ui_tree` (target < 500 ms), asserted in integration tests.
- Deferred from the plan's M4 row: `interactive_only` filtering — the normalizer already removes most noise; add only if agent evals show a need.

## M5 implementation notes (2026-09-30)

- **Target resolution** (shared with M6's control tools): a ref alone; or `hwnd`/ref scope + locator that must match exactly one element. Zero → `ELEMENT_NOT_FOUND`; several → `AMBIGUOUS_MATCH` with up to 10 candidates (`ref`, `control_type`, `name`, `automation_id`) in `details`, so the agent can pick by ref.
- **Combo options without side effects:** `CB_GETCOUNT`/`CB_GETLBTEXT` via `SendMessageTimeout` (`SMTO_ABORTIFHUNG`, 1 s). Verified cross-process on WinForms and native combos; the TestApp event log proves no `DropDown` fired. Owner-drawn combos without `CBS_HASSTRINGS` return no options. Capped at 200 items with `option_count` giving the total (charmap's font list exceeds it).
- **Suggested locator** order: automation id → `control_symbol` → name + control type → automation id + control type; each checked against the whole window, first unique wins, else best candidate with `unique: false`.
- **Symbols:** Visual Studio `resource.h` parsed (decimal/hex/parenthesized, `_APS_*` skipped, UTF-16 files OK). Control lookups consider `IDC_*`, standard IDs (`IDOK`…, `IDC_STATIC` as -1 and 0xFFFF) and unprefixed names only when no `IDC_` name exists — `IDD_`/`ID_`/`IDM_`/`IDS_`/… never. Several names → `control_symbol_candidates`. Live-verified on charmap: `IDD_CHARMAP` sharing 104 with the edit was correctly ignored; `GetDlgCtrlID` = AutomationId = symbol value.
- **FlaUI pitfalls found (both would bite later work):** (1) `FromHandle` inside an active subtree `CacheRequest` becomes `ElementFromHandleBuildCache`, which UIA rejects → get the window element before activating the cache. (2) `CacheRequest` elements are cache-only unless `AutomationElementMode = Full`; live calls on them throw. M6 actions depend on (2).
- **Test infrastructure (after an interrupted run left test apps open):** launched apps join a kill-on-close Windows job (verified: killing the test host closes its apps within ~1.5 s); an assembly-fixture watchdog aborts GUI runs after 5 min; `Microsoft.Testing.Extensions.HangDump` enables `--hangdump` per GUI project. The MSBuild `TestingPlatformCommandLineArguments` property was rejected as the mechanism: .NET 10's native MTP `dotnet test` ignores it.

## M6 implementation notes (2026-09-30)

- **Checks, in order, before any action reaches the target** (unit-tested with a recording fake that must stay empty): mode is control → target resolves through the allowlist gate to exactly one element → element enabled (`ELEMENT_DISABLED`; re-checked live in the Windows layer against races) → no `set_value` on password fields (`PASSWORD_FIELD`). Every attempt is audited, successful or not; password values are redacted.
- **Mechanisms (reported as `method`):** invoke → `uia.InvokePattern` / `TogglePattern` / `SelectionItemPattern` / `ExpandCollapsePattern` / `win32.BM_CLICK` (posted, never sent); set_value → `uia.ValuePattern` (refuses read-only) / `win32.WM_SETTEXT`; select_option → expand, `uia.SelectionItemPattern` by exact then case-insensitive name, collapse back / `win32.CB_SETCURSEL+CBN_SELCHANGE` for combos whose items UIA can't see (the explicit notification is what makes the app's handler run); set_toggle → `uia.TogglePattern` to a target state; `none` when nothing had to change (`changed: false`).
- **Blocking click handlers:** a provider may run the handler synchronously inside `Invoke`; a long handler or modal dialog then exceeds the UIA timeout although the click happened. This is returned as success with a `warning`, not `TARGET_NOT_RESPONDING`, so agents don't click twice (verified with the TestApp Freeze button).
- **`wait_for`** polls every 100 ms (conditions `exists`, `gone`, `enabled`, `text_equals`, `text_contains`; timeout 0–60 s, default 5 s). "Text" = value, else name (status labels). `TIMEOUT` is retryable and reports `last_text`. Refuses text conditions on password fields, which would otherwise let a caller probe their content.
- **Tool annotations:** `invoke` is `destructiveHint: true`, `idempotentHint: false` (a click can do anything); the other control tools are idempotent and non-destructive. Control tools are absent from `tools/list` in observe mode.
- **E2E tests take ~6 s each**, mostly client shutdown after the scenario (the server itself starts in ~0.45 s and exits ~40 ms after stdin closes). Worth trimming before M8's repeated E2E runs.
- Not in M6: `send_keys` (M8), a flag to allow password entry, and modal-dialog handling beyond the warning (M9).

## M7 implementation notes (2026-09-30)

- **PrintWindow only, no screen copy.** The window renders itself (`PW_RENDERFULLCONTENT`, so DirectComposition/GPU content is included), which works when it is covered and **never includes other windows' pixels**. A BitBlt-from-screen fallback was rejected: it would capture whatever overlaps the window, i.e. potentially non-allowlisted content. Verified by covering the TestApp with charmap: client-area pixels unchanged (only the title bar greys when focus moves).
- **Hang safety:** PrintWindow sends `WM_PRINT`, so it blocks on a busy target. Windows already flagged as hung are refused immediately; otherwise the render runs with a 5 s timeout → `TARGET_NOT_RESPONDING` (verified with the Freeze button: refused in < 7 s).
- **Geometry:** the window is rendered at its `GetWindowRect` size (incl. invisible resize borders) and cropped to the DWM visible frame, or to the element's UIA bounds + padding clipped to that frame. `source_bounds` is in physical screen pixels; `scale` ≤ 1 after fitting `max_edge` (default 1280, 64–4096); `dpi` is the window's DPI.
- **Errors:** minimized → `WINDOW_MINIMIZED` (no rendered content); offscreen/zero-size element → `ELEMENT_OFFSCREEN` (new, environment category).
- **DPI above 100% is still unverified.** This session reports 96 DPI on a single 1920×1080 `DISPLAY9` (typical of Remote Desktop) while the user's physical display is at 175%. The server is Per-Monitor-V2 aware and all geometry is physical pixels, so behaviour should hold, but it must be tested with scaling > 100% *inside* the session before calling DPI done (tracked for M8).

## M8 implementation notes (2026-09-30)

- **`send_keys`**: `text` (typed as Unicode characters; newline/tab become Enter/Tab key presses) or `keys` (comma-separated chords, modifiers Ctrl/Shift/Alt, named keys, scan codes and extended-key flags set). Optional element to focus first (`uia.SetFocus`).
  - **Never types into another window**: before every chunk (32 characters or one chord) the foreground window's root must be the target; otherwise `FOCUS_FAILED` with `parts_sent`. Owned dialogs count as "another window" — conservative by design.
  - **Foreground acquisition**: Windows only lets the process that received the last input change the foreground. A zero-distance `SendInput` mouse move qualifies WinMCP without side effects (the common Alt-tap trick would activate the menu bar of whichever app has focus). Worked 3/3 with charmap in the foreground; the charmap edit stayed untouched.
  - Refused by policy: Windows-key chords, `Alt+Tab`, `Alt+Esc`, `Ctrl+Esc`, `Ctrl+Shift+Esc`, `Ctrl+Alt+Del`; password targets and a focused password field (checked after focusing).
- **E2E speed**: the MCP SDK client waits `ShutdownTimeout` (default 5 s) for the server to exit *before* closing its stdin, while the server exits ~30 ms after stdin closes. Setting 500 ms in the test helper took the E2E suite from 13 s to 4 s. Real clients close stdin themselves.
- **Manual testing without locking builds**: `scripts\publish-mcp.ps1` also publishes the TestApp to `artifacts\testapp` (same junction scheme). A TestApp launched from `bin\Release` blocks every build of it — hit during M8.
- **Docs**: `docs/tools.md` (reference with examples), `docs/limitations.md` (measured limits), `docs/security.md` (model and guarantees).
- **Remote**: `origin` = `git@github.com:d093w1z/WinMCP.git` (push pending SSH key setup on this machine).
- **DPI at 150% — found and fixed two bugs.** (1) WinMCP library: geometry depended on the host process being DPI-aware; in the DPI-unaware test runner, `GetWindowRect`/UIA returned scaled values while DWM returned physical ones, so a capture used 259×197 of a 484×387 window. `DpiScope` now runs all Win32 geometry calls Per-Monitor-V2, and the UIA worker thread is PMv2 for life; the server exe was already PMv2 via manifest, so it was likely unaffected, but the library no longer depends on it. (2) TestApp: layout built in code without `AutoScaleDimensions` → fonts scaled, layout didn't, captions clipped. Fixed; captures inspected at 150%.
- **Stability: 20/20 consecutive runs of the integration (54) and E2E (9) suites passed at 150% scaling** (~63 s per run, no leftover processes). Log: `artifacts\test-results\stability.log`.

### MVP criteria status (§ "MVP definition")

| # | Criterion | Status |
|---|-----------|--------|
| 1 | Clean build + Core/Server tests | ✅ 248 + 26 pass |
| 2 | Integration tests 20 consecutive runs, 0 failures | ✅ 20/20 at 150% scaling (54 tests; the plan's 20 are covered and extended) |
| 3 | Golden scenario E2E over stdio, audit shows only semantic methods | ✅ `GoldenScenarioE2ETests` |
| 4 | Claude Code solves the MVP prompt in ≥ 4/5 fresh sessions without screenshots | ✅ **5/5** (2026-09-30, 12:06–12:11): each run exactly `set_value` → `select_option` → `invoke`, all `ok` with UIA methods, no errors or retries; correct status reported; no `capture_screenshot` |
| 5 | Tool surface; observe mode lists only observe tools | ✅ 12 tools: 7 observe + 5 control (`send_keys` included) |
| 6 | Security defaults tested | ✅ allowlist, `WINDOW_NOT_FOUND` for others, `send_keys` never types elsewhere (charmap test) |
| 7 | Performance: tree < 500 ms, interactions < 300 ms | ✅ TestApp tree ~100–150 ms (asserted); actions 5–15 ms in M0/M6 measurements |
| 8 | Docs: README, tools, limitations, security | ✅ |

---

## Sources (checked 2026-09-30)

- .NET support policy — https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- ModelContextProtocol NuGet — https://www.nuget.org/packages/ModelContextProtocol
- MCP C# SDK tools docs — https://csharp.sdk.modelcontextprotocol.io/v1/concepts/tools/tools.html
- MCP spec, tools (2025-11-25) — https://modelcontextprotocol.io/specification/2025-11-25/server/tools
- .NET MCP server quickstart / NuGet publishing / `dnx` — https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/build-mcp-server
- FlaUI releases — https://github.com/FlaUI/FlaUI/releases · https://www.nuget.org/packages/FlaUI.UIA3/
- CsWin32 — https://www.nuget.org/packages/Microsoft.Windows.CsWin32
- xUnit v3 — https://www.nuget.org/packages/xunit.v3
