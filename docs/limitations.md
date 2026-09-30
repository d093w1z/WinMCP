# Known limitations

What WinMCP can't do (yet), measured on Windows 11 26100 with .NET 10 unless noted. Each item says what happens and what to do about it.

## UI Automation coverage

- **Custom-drawn controls are opaque.** A control that paints itself without an accessibility provider appears as one element with no children (e.g. charmap's character grid: a single `Pane`). WinMCP can report its bounds and capture a screenshot of it, but can't read or act on its contents. Planned: merge child HWNDs that UIA doesn't expose into the tree (plan §A.5).
- **Labels come from heuristics.** Edits and combos get their name from the preceding label (`"Name:"`); `labeled_by` is usually empty for WinForms. Dialogs whose label order differs from the input order may get misleading names — prefer `automation_id` / `control_symbol` locators.
- **Collapsed combo boxes expose no items** to UIA. WinMCP reads options with Win32 messages instead (`inspect_element`), and opens/closes the combo only while selecting. Owner-drawn combos without stored strings have no readable options; at most 200 options are returned (`option_count` gives the total).
- **Win32 fallbacks exist only where they reach the app's handlers**: `BM_CLICK` (posted), `WM_SETTEXT`, and `CB_SETCURSEL` + an explicit `CBN_SELCHANGE` (without it the app never sees the change).

## Large windows

- Fetching a tree costs the target roughly **2.5 ms per element for WinForms** and ~1.3 ms for native Win32 (measured: 634 WinForms elements ≈ 1.5 s), and this is the application's own traversal — requesting fewer properties doesn't help. A window with thousands of elements (e.g. a list with thousands of rows) can exceed the 3 s UI Automation timeout while perfectly healthy. WinMCP then reports `TARGET_NOT_RESPONDING` with a hint that the tree is probably too large. Planned for M9: fetch level by level and stop at `max_nodes`.

## Hung and busy applications

- UI Automation calls time out after **3 s** per call and 10 s per tool call; a stuck call's worker thread is abandoned so the server stays usable. Windows itself flags a window as not responding only after ~5 s, so `inspect_window.responding` lags.
- A click whose handler blocks (long work, a modal dialog) is reported as success with a `warning` after ~3 s. The dialog is visible through `list_windows` / `inspect_window.owned_windows`; driving dialogs is planned for M9.
- Screenshots of busy windows time out after 5 s (`PrintWindow` has to be answered by the application).

## Keyboard input (`send_keys`)

- Input goes to the **foreground** window, so WinMCP must make the target foreground first. Windows restricts this; WinMCP uses a zero-distance mouse move to qualify, which worked in every test run, but Windows may still refuse (e.g. while the user is typing elsewhere) → `FOCUS_FAILED`, with nothing typed.
- If another window takes the foreground mid-input, WinMCP stops (`FOCUS_FAILED`, `parts_sent` in details); the part already typed is not undone.
- Text is sent as Unicode characters (layout-independent). Applications that read raw scan codes (some games, remote-desktop clients) may ignore it.
- Keys never available: anything with the Windows key; `Alt+Tab`, `Alt+Esc`, `Ctrl+Esc`, `Ctrl+Shift+Esc`, `Ctrl+Alt+Del`.

## Elevation, sessions and desktops

- WinMCP runs at the user's normal integrity level. **Windows' UIPI blocks UI Automation and input to elevated (administrator) applications**; such processes show `elevated: null` or `true`. Running WinMCP itself elevated is possible but not recommended. A clear `ACCESS_DENIED_ELEVATED` error is planned.
- The secure desktop (UAC prompts, lock screen) is never accessible, by Windows design and by WinMCP's deny-list.
- GUI tests need an unlocked, interactive desktop. Whether CI-hosted runners provide one reliably is still open (the CI job for GUI tests is marked experimental).

## Display scaling

- WinMCP is Per-Monitor-V2 DPI aware and reports all geometry in **physical pixels**; `dpi` is reported per window. **Scaling above 100% has not been verified yet** (the development session runs at 96 DPI over Remote Desktop).
- Coordinates on multi-monitor setups can be negative (virtual-screen space).

## Frameworks

- Verified: WinForms, classic Win32 dialogs. MFC is expected to behave like Win32 for standard controls (planned verification: M10/M11 with an MFC test app). WPF, WinUI, Qt and Chromium/Electron apps work to the extent their UIA providers do; they have not been tested.

## Not implemented (by design, for now)

Event/message recording, launching or closing applications, coordinate-based clicking, full-desktop screenshots, HTTP transport, reading debugging information (logs, ETW, crash dumps).
