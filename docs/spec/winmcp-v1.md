# WinMCP v1 specification

| | |
|---|---|
| Status | v1.0.0, 2026-10-01 |
| Supersedes | The design sections of `docs/design/mvp-plan.md`, which remains the record of how v1 was reached (decisions, measurements, milestone notes) |
| Companion documents | `docs/tools.md` (tool reference), `docs/security.md`, `docs/limitations.md`, `docs/mfc-investigation.md` |

This document defines what WinMCP v1 is: its scope, architecture, security model, tool contracts, behaviour guarantees, distribution and quality bar. Everything stated here is implemented and covered by tests; [§16](#16-decisions-for-v1-and-later-releases) records the decisions taken for v1 and what is left to later releases. "Must" marks a requirement that a change may not break without revising this spec.

---

## 1. Purpose and scope

WinMCP is a local [Model Context Protocol](https://modelcontextprotocol.io) server that gives AI agents **semantic** access to native Windows desktop applications — windows, controls, values and states — instead of screen coordinates. An agent finds a window, reads its control tree, acts on controls by reference (click, set text, select, toggle, expand, type) and verifies the result.

**Goals**

1. Let an agent operate standard Win32, WinForms and MFC applications reliably through UI Automation, with Win32 fallbacks where UI Automation is insufficient.
2. Help developers inspect and debug their own native applications: control IDs with `resource.h` names, dialog templates, framework facts.
3. Be safe by default: read-only unless enabled, only explicitly allowed applications, never another application's data.
4. Report honestly: every result says how it was obtained, and WinMCP never claims an effect it did not observe.

**Non-goals for v1** (see also §15): recording events, launching or closing applications, coordinate-based input, remote access, reading logs or debugging targets, injecting code into target processes.

## 2. Users and scenarios

| User | Scenario |
|---|---|
| AI agent (e.g. Claude Code) | "Fill in this form and apply it", "what does the status bar say", "which options are enabled" — driving an allowlisted application through MCP tools |
| Developer of a Win32/MFC application | Inspecting their own app: which dialog template a window came from, which controls were added or hidden at run time, `IDC_*` names for numeric IDs, which MFC classes are likely behind windows |
| Test author | Deterministic, locator-based automation with explicit errors instead of silent misses |

## 3. Supported environment

| Aspect | v1 support |
|---|---|
| Operating system | Windows 11 x64 (verified on 26100); Windows 10 x64 expected to work |
| Runtime | .NET 10 (LTS); the server is `net10.0-windows10.0.17763.0` |
| Client | Any MCP client that launches stdio servers (verified: Claude Code) |
| Session | An interactive, unlocked desktop session of the signed-in user |
| Target applications | Non-elevated processes of the same user. **Verified:** Win32 dialogs, WinForms, MFC (shared and static; dialog, SDI frame, Feature Pack controls), and a commercial MFC application built on BCGControlBar. **Unverified:** WPF, WinUI, Qt, Chromium/Electron (they work to the extent of their UI Automation providers) |
| Target bitness | x64. x86 targets are expected to work (UI Automation and resource reading are bitness-independent) but are not part of v1's verified scope |
| Display | Physical pixels throughout (Per-Monitor-V2); verified at 100 % and 150 % on one monitor. Mixed-DPI multi-monitor setups are not part of v1's verified scope |
| Server platform | `win-x64` release package. `win-arm64` is left to a later release |

## 4. Architecture

### 4.1 Components

```
MCP client ──stdio (JSON-RPC; logs on stderr)──▶ WinMcp.Server
                                                   │ tool adapters, request filters, DI
                                                   ▼
                                              WinMcp.Core        no Windows or MCP dependencies
                                                   │ interfaces: IDesktop, IUiAutomation, IKeyboard,
                                                   │             IScreenCapture, INativeProcesses, IAuditLog
                                                   ▼
                                              WinMcp.Windows     Win32 (CsWin32), UI Automation (FlaUI.UIA3)
```

- **Server** knows MCP, not Windows. **Windows** knows Windows, not MCP. **Core** knows neither, and holds all policy, tree, locator, symbol and matching logic — this keeps the logic unit-testable with fakes.
- Dependencies: `ModelContextProtocol` 2.2, `FlaUI.UIA3` 5.0 (confined to `WinMcp.Windows`), `Microsoft.Windows.CsWin32`, `System.Drawing.Common` (PNG encoding).

### 4.2 Threading and responsiveness

- All UI Automation work runs on one dedicated MTA thread. Each UI Automation call times out after **3 s**; each tool call after **10 s**, after which the worker thread is abandoned and replaced, so a hung target can never wedge the server.
- A tree is fetched in **one** cross-process round trip (UI Automation `CacheRequest` over the control view). Bounds are not part of the bulk fetch; they are read per element when needed.
- Responsiveness is decided by a direct `WM_NULL` ping (500 ms): not answering → `TARGET_NOT_RESPONDING`; answering while UI Automation times out → Win32 fallback (§8.5).

### 4.3 Element identity

- An element is identified by its top-level window and its UI Automation runtime id. Elements without a runtime id (Win32 menu bar items of an inactive window) get a synthesized identity from their parent's identity, position and name, computed identically by tree fetches and live lookups.
- Elements of the Win32 fallback tree are identified by their window handle.

## 5. Security model

The following must hold in every release:

1. **Transport:** stdio only. WinMCP opens no network listener.
2. **Modes:** `observe` (default) registers only read-only tools; `control` adds the interaction tools. In observe mode control tools are absent from `tools/list`, not merely refused.
3. **Allowlist:** only windows of processes named by `--allow` (process name or full executable path) are visible or actionable. An empty allowlist means nothing is visible; control mode refuses to start without one.
4. **No disclosure about other applications:** the allowlist is applied before caller filters; windows of other processes never appear in any result; their handles yield `WINDOW_NOT_FOUND`, identical to nonexistent windows. Only an aggregate `excluded_count` is reported.
5. **Deny-list:** UAC (`consent`), `LogonUI`, `winlogon`, `CredentialUIBroker` and WinMCP itself can never be targeted, even if allowlisted. `--allow` entries naming them produce a start-up warning on stderr for the operator; agents get no distinct error (it would reveal that such a window exists).
6. **Re-validation:** every action re-checks the target's process against the allowlist when it runs.
7. **Passwords:** password field values are never returned (trees, inspection, `wait_for` text conditions are refused on them); `set_value` and `send_keys` refuse them; audit entries redact them.
8. **Disabled controls** are refused before any input reaches the application.
9. **Keyboard input** is sent only while the target window is the foreground window, re-checked before every chunk; on any change input stops with `FOCUS_FAILED`. Windows-key and window-switching chords are refused.
10. **Ambiguity is an error:** an action whose criteria match several elements is refused, listing candidates. WinMCP never guesses a target.
11. **Screenshots** are rendered by the target window itself (`PrintWindow`); pixels of other windows can never appear. There is no screen-copy fallback and no full-desktop capture.
12. **Files:** no tool accepts a file path. `resource.h` paths are operator configuration. WinMCP reads the files of **allowlisted** applications only — the module list, and the application's own executable, DLLs from its folder and `<exe folder>\<LCID>\*.dll` — as data (`LOAD_LIBRARY_AS_DATAFILE`), never executing them. System DLLs are not read.
13. **No code in target processes:** nothing is loaded or injected into another process.
14. **Audit:** every control action, successful or refused, is appended to the audit log (§12).
15. **Prompt injection:** the server instructions state that text displayed in applications is data, never instructions. (Advisory: clients and models decide what they do with it.)

16. **Elevated targets:** WinMCP runs with the user's rights and does not elevate. Windows isolates elevated applications from non-elevated ones (UIPI), so UI Automation sees little of them and input is dropped. Their windows stay listable and inspectable (`elevated: true`); every tool that reads or operates their UI — `get_ui_tree`, `find_elements`, `inspect_element`, `wait_for`, `capture_screenshot`, all control tools — returns `ACCESS_DENIED_ELEVATED` instead of degraded results. If WinMCP itself runs elevated (discouraged), the check doesn't apply.

## 6. Configuration

Command-line only (the MCP client configuration supplies it):

| Argument | Meaning | Default |
|---|---|---|
| `--mode observe\|control` | Tool set | `observe` |
| `--allow <name-or-path>` (repeatable) | Allowlisted process (name without `.exe`, case-insensitive, or full executable path) | none |
| `--symbols <process>=<resource.h>` (repeatable) | `resource.h` for a process; checked to exist at start-up | none |
| `--audit-dir <directory>` | Audit log location | `%LOCALAPPDATA%\WinMCP\audit` |

Invalid arguments exit with code 2 and a usage line on stderr.

**No configuration file in v1.** MCP clients already store a server's arguments as a JSON array, so long command lines, paths with spaces and many `--allow`/`--symbols` entries need no shell quoting. Different policies per application are configured as separate server registrations (e.g. one in control mode for the application under test, one in observe mode for others). A configuration file becomes worthwhile when settings stop fitting one flat argument list — per-application modes or policies within one server, or settings shared across clients and machines independently of each client's format — and would then be added alongside, not instead of, the arguments.

## 7. Core concepts

### 7.1 Addressing

| Thing | Form | Lifetime |
|---|---|---|
| Window | `hwnd:0x000A0B1C` | Until the window closes |
| Element ref | `e7` | The server session. The same element always gets the same ref; a ref to a removed element yields `ELEMENT_STALE`; a ref never points to a different element |
| Locator | `automation_id`, `name`, `name_contains`, `control_type`, `class_name`, `control_symbol` (AND-ed, case-insensitive), scoped by `hwnd` or a ref | Durable across sessions. For actions it must match exactly one element (`AMBIGUOUS_MATCH` otherwise) |

`inspect_element` suggests a durable locator, verified unique: the `resource.h`/MFC symbol if known, else the automation id, else name + control type.

### 7.2 UI tree

- The control view of UI Automation, normalized: title bars and combo-box internals removed, anonymous single-child wrappers collapsed, list-view row cells folded into the row value (`"Beta | HTML | 2 KB"`).
- Rendered as a compact outline (`[e3] Edit "Name:" #nameTextBox value="" focused`) plus the same tree as JSON. States: `disabled`, `offscreen`, `focused`, `password`, `collapsed`/`expanded`/`partially_expanded`, `on`/`off`/`indeterminate`, `selected`.
- Limits: `max_depth` (default 10, ≤ 50), `max_nodes` (default 300, ≤ 2000). Truncated subtrees are marked and can be fetched by ref.
- `source` is absent for UI Automation trees and `win32` for fallback trees (§8.5).

### 7.3 Symbols

For Win32/MFC controls UI Automation exposes the control ID as automation id. WinMCP names IDs:

- From a configured `resource.h`: `IDC_*` (and unprefixed names) for controls, `ID_*`/`IDM_*` for menu commands, `IDD_*` for dialog templates. Several names for one ID are reported as candidates, never chosen between.
- For MFC processes, without configuration: MFC's standard frame window IDs (`AFX_IDW_TOOLBAR`, `AFX_IDW_STATUS_BAR`, `AFX_IDW_PANE_FIRST`, …) and framework commands (`ID_FILE_NEW`, `ID_APP_EXIT`, …) from `afxres.h`. The application's own names take precedence.
- Windows' standard dialog IDs (`IDOK` … `IDCONTINUE`) name buttons that are direct children of a dialog; `IDC_STATIC` names any non-dialog control of a dialog. No standard ID names a dialog window.
- A symbol is given only where the automation id *is* the control ID (IDs are unique among siblings only).
- Symbols work in outlines, `find_elements`, `inspect_element` and as `control_symbol` locators.

## 8. Behaviour guarantees for actions

### 8.1 Results

Every control tool returns `{ok, action, element, method, changed, value_after?, state_after?, warning?, elapsed_ms}`.

- `method` names the mechanism (`uia.InvokePattern`, `uia.ValuePattern`, `win32.BM_CLICK`, `win32.CB_SETCURSEL+CBN_SELCHANGE`, `win32.SendInput`, …) or `none` when nothing had to change.
- `set_toggle`, `set_expanded` and `select_option` take a **target state**: repeating them is harmless (`changed: false`).
- `value_after` / `state_after` are read back from the control, after waiting up to 1 s for asynchronous changes (native menus open asynchronously).

### 8.2 Honest effects

Where an effect is observable, WinMCP verifies it instead of trusting the control:

- A selection that doesn't take is an error (`PATTERN_NOT_SUPPORTED`, with a hint to use keyboard input).
- `invoke` on a tab that stays unselected, or on a menu command whose menu stays open, returns `changed: false` with a `warning` (observed: BCGControlBar ribbons and menus accept UI Automation requests and ignore them).
- `invoke` on an already-selected item returns `method: none`, `changed: false`.

### 8.3 Mechanisms chosen per control

| Situation | Mechanism | Reason |
|---|---|---|
| Win32 push button (`BUTTON` class) | `BM_CLICK`, sent with a 750 ms timeout; a still-running handler yields a `warning` | Through UI Automation, frameworks may run the handler inside the call; a modal dialog then blocks UI Automation for the whole application |
| Plain Win32 combo box | `CB_SETCURSEL` + explicit `CBN_SELCHANGE` | UI Automation's selection doesn't notify the application, so its logic never runs |
| Combo box options | Read with `CB_GETLBTEXT`, without opening the combo (≤ 200 returned, `option_count` gives the total) | Collapsed combos expose no items to UI Automation |
| Native menu item in an inactive window | Activate the window first, as a click would; `FOCUS_FAILED` if Windows refuses | Native menus open only in the active window |
| comctl32 toolbar button | UI Automation Invoke, then restore the previous keyboard focus | UI Automation leaves focus on the toolbar; MFC frames then can't open their menus |
| Tree item given as a path (`"A > B > C"`) | Expand each ancestor, then select | Children of collapsed nodes aren't in the tree |
| Everything else | The element's UI Automation pattern | |

### 8.4 Keyboard input

`send_keys` sends either literal text (Unicode, layout-independent; newlines and tabs as real Enter/Tab presses) or chords (`"Ctrl+A, Backspace, Enter"`), optionally focusing an element first. It brings the window to the foreground (two attempts) and stops at the first chunk where the foreground changed, reporting how much was sent. Nothing typed is ever undone.

### 8.5 Win32 fallback

When UI Automation times out on a window that still processes messages (typically: a modal dialog opened from inside a UI Automation call), tree and find tools return the window's child windows (`source: "win32"`), and actions on those refs use Win32 messages (`BM_CLICK`, `WM_SETTEXT`, combo selection, check boxes). When UI Automation answers again, fallback refs become stale.

## 9. Tools

Thirteen tools. All results are JSON (`snake_case`) as `structuredContent` plus text, except `get_ui_tree` (text is the outline) and `capture_screenshot` (an image block plus JSON). Unknown parameters are rejected with `INVALID_ARGUMENT` listing the valid ones. Full field lists: `docs/tools.md`.

### 9.1 Observe tools (always available)

| Tool | Contract |
|---|---|
| `list_windows` | Top-level windows of allowlisted applications. Filters: `process_name`, `title_contains`, `pid`, `include_hidden`. Returns windows (handle, title, class, process {pid, name, path, architecture, elevated}, visibility/cloaking/state flags, bounds, dpi, owner), `excluded_count`, and a hint when empty |
| `inspect_window` | One top-level window: responding, decoded styles, owned windows (open dialogs), child-window summary, `framework_hint` + `framework` {name, linkage, version, evidence, libraries} (§10.1), `dialog_resources` (§10.2) |
| `get_ui_tree` | Normalized tree of a window or subtree (§7.2) |
| `find_elements` | Elements matching a locator, with refs, `count`, `truncated` (`max_results` default 25) |
| `inspect_element` | One element in full: value, states, class, framework id, own and host window handle, control ID and symbol (or candidates), supported patterns, focusability, combo options, bounds, parent, `labeled_by`, help text, suggested locator, and `mfc_class_guess` for window-backed elements of MFC applications (§10.3) |
| `wait_for` | Poll until `exists`, `gone`, `enabled`, `text_equals` or `text_contains` (text = value, else name); `timeout_ms` 0–60 000 (default 5000); `TIMEOUT` reports the last text seen |
| `capture_screenshot` | PNG of a window or element (`padding`, `max_edge` default 1280), rendered by the window itself, with source bounds, scale and dpi |

### 9.2 Control tools (`--mode control`)

| Tool | Contract |
|---|---|
| `invoke` | Activate an element as a click would (button, menu item, check box, item, expand/collapse); §8.3 |
| `set_value` | Replace a field's text; read-only values and password fields refused |
| `select_option` | Select an item by text in a combo box, list, tab control or tree (target the container); tree paths supported; unknown option → `OPTION_NOT_FOUND` with the available options |
| `set_toggle` | Set a check box `on`/`off` |
| `set_expanded` | Set a tree node, menu or combo box `expanded`/`collapsed` |
| `send_keys` | Text or chords to the foreground-verified window (§8.4) |

Control tools address their target by ref or by `hwnd` + `automation_id` / `name` / `control_type` / `control_symbol`. Annotations: observe tools are read-only and idempotent; control tools are marked accordingly (`invoke` destructive) for client confirmation prompts — hints, not enforcement.

## 10. Native application facts

Read-only and out of process (§5.12). Cached per process (modules, 30 s) and per file (templates, by timestamp).

### 10.1 Framework detection

| Signal | Result |
|---|---|
| Loaded `mfc*.dll` (e.g. `mfc140u.dll`) | `mfc`, linkage `shared`, version of the DLL |
| `Afx…` window classes without an MFC DLL | `mfc`, linkage `static` |
| No MFC DLL and no MFC classes, but `CCmdTarget`/`CWinThread` inside the executable | `mfc`, linkage `static` (static MFC dialog applications) |
| `BCGP*` classes / `BCGCB*` DLLs; `XTP*` classes / `ToolkitPro*` DLLs | `libraries`: BCGControlBar; Codejock Xtreme Toolkit |
| Window class names | `winforms`, `wpf`, `winui3`, `uwp`, `chromium`, `qt`, `java-awt`, `win32-dialog` |

Each result lists its evidence. If the process can't be read (elevated), only window classes are used.

### 10.2 Dialog templates

`RT_DIALOG` resources (`DLGTEMPLATE`/`DLGTEMPLATEEX`, MUI-aware) of the application's own files are matched to the live window and to dialogs inside it (property pages, form views) by control-ID similarity (Jaccard over IDs other than 0 and `IDC_STATIC`; ≥ 0.5, at least two shared IDs unless the caption also matches). Reported: resource ID and `IDD_*` symbol, module, match, template caption, controls **extra** (created at run time), **hidden**, **missing**, and equally good **alternatives** instead of a guess.

### 10.3 Likely MFC class

For MFC processes, `mfc_class_guess` maps window class and control ID to the MFC class family (`CDialog`, `CFormView`, `CListCtrl`, `CToolBar`, `CStatusBar`, `CView`, `CFrameWnd`, `CMDIFrameWnd`, `CMFCPropertyGridCtrl`, …). It is an inference: application subclasses appear as their MFC base, and a control may have no C++ object at all. Exact runtime classes would require code in the target process and are out of scope (§15).

## 11. Errors

Failures are tool results with `isError: true` and `{"error": {code, message, category, retryable, hint?, details?}}`.

| Category | Codes |
|---|---|
| `caller` — change the arguments | `INVALID_ARGUMENT`, `WINDOW_NOT_FOUND`, `ELEMENT_NOT_FOUND`, `AMBIGUOUS_MATCH`, `OPTION_NOT_FOUND`, `PATTERN_NOT_SUPPORTED`, `ELEMENT_DISABLED`, `ELEMENT_STALE` |
| `policy` — refused by WinMCP's rules | `OPERATION_NOT_PERMITTED`, `PASSWORD_FIELD` |
| `environment` — application or desktop state | `ACCESS_DENIED_ELEVATED`, `TARGET_NOT_RESPONDING`, `WINDOW_MINIMIZED`, `ELEMENT_OFFSCREEN`, `WINDOW_CLOSED`, `FOCUS_FAILED`, `TIMEOUT` |
| `internal` — a WinMCP bug; details only in the server log | `INTERNAL_ERROR` |

`retryable` is true only for `TARGET_NOT_RESPONDING`, `FOCUS_FAILED` and `TIMEOUT`: the same call may succeed later unchanged. There is deliberately no "not allowed" code: a window of a non-allowlisted application is `WINDOW_NOT_FOUND` in every case, including a handle reused by another process mid-session (§5.4). Hints are written for the agent (what to do next); `FOCUS_FAILED` hints distinguish a locked desktop from a refused foreground switch.

## 12. Audit log

One JSON object per line in `<audit-dir>\audit-YYYYMMDD.jsonl` for every control action, successful or refused: `timestamp`, `tool`, `window`, `process`, `element`, `target`, `arguments` (password values redacted), `outcome` (`ok` or the error code), `method`, `elapsed_ms`. The log is local and not tamper-proof; it supports review and failure triage.

## 13. Distribution and versioning

- **Release package:** `WinMCP-<version>-win-x64.zip` with a self-contained, single-file `WinMcp.Server.exe` (no .NET installation needed on the user's machine; not trimmed, because UI Automation is COM interop) plus `README.md`, `LICENSE` and the user documentation (`tools.md`, `security.md`, `limitations.md`), and a `.sha256` checksum file. Built by `scripts/package.ps1`.
- **Installation:** extract anywhere and register the executable with an MCP client; the README gives the configuration for Claude Code, VS Code, Cursor and Claude Desktop, the arguments, usage, update and removal.
- **Versioning:** Semantic Versioning, defined once in `Directory.Build.props` and reported as the MCP server version. Breaking changes to tool names, parameters, result fields or error codes require a new major version; additions are minor.
- **Release process:** pushing a tag `vX.Y.Z` that matches the version runs `.github/workflows/release.yml`: build, non-GUI tests, packaging, the end-to-end suite **against the packaged executable**, and publication of the archive and checksum as a GitHub release.
- **History:** `main` carries one commit per release; development happens on `dev`.

## 14. Quality bar

### 14.1 Performance (measured on the reference machine)

| Operation | Bound |
|---|---|
| `get_ui_tree` on a typical dialog/form | < 500 ms |
| A single interaction (excluding `wait_for`) | < 300 ms median |
| Tree or find on a 10 000-row list (~40 000 elements) | < 4 s (measured 2.4 s); lists up to ~30 000 rows stay within the UI Automation timeout |
| Hung target | `TARGET_NOT_RESPONDING` within ~3 s; the server stays responsive |

### 14.2 Tests

- **Unit** (`WinMcp.Core.Tests`): policy, normalization, locators, symbols, framework detection, template parsing and matching, interaction rules, elevation gate — with fakes.
- **Server** (`WinMcp.Server.Tests`): MCP client ↔ server in memory with a fake desktop: tool surface per mode, schemas, error mapping, argument validation.
- **Integration** (`WinMcp.Windows.IntegrationTests`): real UI Automation against the WinForms test app, the MFC test app (dialog, frame, features; shared and static MFC) and charmap. A shared scenario suite runs against both test apps.
- **End-to-end** (`WinMcp.E2ETests`): the real server over stdio — the development build in CI, the packaged executable in the release workflow.
- Regression tests reproduce each fixed real-world finding.

### 14.3 Acceptance for v1

1. All test projects green on an unlocked interactive desktop; GUI suites stable over 20 consecutive runs.
2. CI green, with **both jobs required**: build + non-GUI tests, and the GUI suite (WinForms, MFC, end-to-end) on GitHub-hosted Windows runners.
3. Agent evaluations pass: the WinForms golden scenario (≥ 4 of 5 fresh sessions, no screenshots), the MFC dialog and frame scenarios, and an observe + control exploration of a real-world application without false success reports.
4. The release package passes the end-to-end suite.
5. `README.md`, `docs/tools.md`, `docs/security.md` and `docs/limitations.md` match the implementation.

## 15. Out of scope for v1

Event and message recording (including accessibility events such as focus notifications); launching, closing or restarting applications; coordinate-based clicks and drags; full-desktop screenshots; HTTP or any remote transport; logs, ETW, crash dumps, debugger attachment; in-process inspection or injection (researched in M11: works for shared MFC only, rejected); framework-specific work for WPF, WinUI, Qt or Electron; AI-driven exploration or test generation.

The architecture leaves room for events (an event source in `WinMcp.Windows` plus tools in the server), process management (a separately gated mode) and diagnostics (a separate tool class).

## 16. Decisions for v1 and later releases

| Item | v1 decision |
|---|---|
| Elevated targets | `ACCESS_DENIED_ELEVATED` for reading or operating the UI of elevated applications; window facts stay available (§5.16) |
| "Not allowed" error | `TARGET_NOT_ALLOWED` removed from the error model: every case it could cover must be `WINDOW_NOT_FOUND` to avoid disclosure (§5.4, §11). Deny-listed `--allow` entries are reported to the operator at start-up instead |
| `control_symbol` | Optional extra detail. MFC standard IDs always work; application names need `--symbols`; the numeric `automation_id` works either way. Tool descriptions say so |
| Packaging | Self-contained `win-x64` archive with install and client-configuration instructions (§13) |
| Configuration file | Not in v1; trigger conditions in §6 |
| CI | GUI job required (§14.3) |
| Opaque property grids | Measured: keyboard focus inside `CMFCPropertyGridCtrl` exposes nothing to UI Automation; MFC reports the focused property only through accessibility events. Not feasible without event support (§15) |

**Left to later releases:** x86 targets, `win-arm64` packages, mixed-DPI multi-monitor setups, broader real-world application coverage (especially satellite resource DLLs); accessibility-event support (would also make focused property-grid rows readable); a NuGet MCP server package, code signing and winget.

## 17. Known limitations (summary)

Details and workarounds: `docs/limitations.md`.

- Custom-drawn content (canvases, 3D views, custom "DirectUI" panels, property-grid rows) is opaque; screenshots are the only view.
- Names of inputs come from UI Automation's label heuristics and can be wrong; prefer automation-id or symbol locators.
- Some controls accept UI Automation requests and ignore them (BCGControlBar ribbon tabs and menu items); WinMCP reports this rather than succeeding, but has no UI Automation route to activate them — keyboard shortcuts are the workaround.
- Ribbons expose only the selected tab; application menus only while open.
- Every call fetches the whole window tree; very large windows approach the UI Automation timeout.
- Foreground changes can be refused by Windows (and are impossible on a locked desktop), which affects `send_keys` and native menus.
- Elevated applications can be listed and inspected but not read or operated; the secure desktop is out of reach.
