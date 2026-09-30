# Known limitations

What WinMCP can't do (yet), measured on Windows 11 26100 with .NET 10 unless noted. Each item says what happens and what to do about it.

## UI Automation coverage

- **Custom-drawn controls are opaque.** A control that paints itself without an accessibility provider appears as one element with no children (e.g. charmap's character grid: a single `Pane`). WinMCP can report its bounds and capture a screenshot of it, but can't read or act on its contents. Planned: merge child HWNDs that UIA doesn't expose into the tree (plan §A.5).
- **Labels come from heuristics.** Edits and combos get their name from the preceding label (`"Name:"`); `labeled_by` is usually empty for WinForms. Dialogs whose label order differs from the input order may get misleading names — e.g. a tab control without an accessible name was called `"Events: 0 []"` after an unrelated label (M9). Prefer `automation_id` / `control_symbol` locators.
- **Modal dialogs opened from inside UI Automation calls block UIA for the whole application** (WinForms menu items, observed in M9). WinMCP falls back to Win32 controls meanwhile (see tools.md); the first call after such a click takes ~3 s (the UIA timeout) before falling back. Push buttons avoid the problem entirely: they're clicked with `BM_CLICK`.
- **Collapsed tree nodes hide their children** from UIA; use `set_expanded` or a `select_option` path.
- **Collapsed combo boxes expose no items** to UIA. WinMCP reads options with Win32 messages instead (`inspect_element`), and opens/closes the combo only while selecting. Owner-drawn combos without stored strings have no readable options; at most 200 options are returned (`option_count` gives the total).
- **Win32 fallbacks exist only where they reach the app's handlers**: `BM_CLICK` (sent with a short timeout), `WM_SETTEXT`, and `CB_SETCURSEL` + an explicit `CBN_SELCHANGE` (without it the app never sees the change). Plain Win32 combo boxes (MFC, dialog resources) are always selected this way, because UI Automation's own selection doesn't notify the application (M10).
- **Toolbar buttons** (comctl32) are clicked through UI Automation, which leaves keyboard focus on the toolbar; WinMCP puts focus back afterwards, since MFC frames won't open their menus while the toolbar has it (M10).

## Large windows

- **Every tool call fetches the window's whole UI Automation tree**; `max_nodes` limits what is returned, not what is fetched. Cost depends on the kind of element:
  - **Window-backed controls are expensive**: ~2.5 ms each for WinForms (634 buttons ≈ 1.5 s), dominated by the application's own work per HWND.
  - **Windowless items are cheap**: a 10,000-row list (≈ 40,000 elements) takes ~2.4 s for `get_ui_tree` / `find_elements` and ~4.7 s for `select_option` (measured M9b, after removing bounds from the bulk fetch).
  - The single UIA call behind a fetch must finish within the 3 s UIA timeout; for the 10,000-row list it takes ~1 s, so lists up to roughly 30,000 rows should work. Beyond that, WinMCP falls back to the window's Win32 controls (the list then appears as one `List` element).
- A true node budget (walk the tree and stop at `max_nodes`, ~0.25 ms per element measured) is a possible future optimization; it doesn't help `find_elements`, which must search everything.

## Hung and busy applications

- UI Automation calls time out after **3 s** per call and 10 s per tool call; a stuck call's worker thread is abandoned so the server stays usable. Windows itself flags a window as not responding only after ~5 s, so `inspect_window.responding` lags.
- A click whose handler blocks (long work, a modal dialog) is reported as success with a `warning` (after 0.75 s for push buttons, ~3 s otherwise). A dialog it opened is visible through `list_windows` / `inspect_window.owned_windows` and can be operated by its own `hwnd`.
- Screenshots of busy windows time out after 5 s (`PrintWindow` has to be answered by the application).

## Keyboard input (`send_keys`)

- Input goes to the **foreground** window, so WinMCP must make the target foreground first. Windows restricts this; WinMCP uses a zero-distance mouse move to qualify, which worked in every test run, but Windows may still refuse (e.g. while the user is typing elsewhere) → `FOCUS_FAILED`, with nothing typed.
- If another window takes the foreground mid-input, WinMCP stops (`FOCUS_FAILED`, `parts_sent` in details); the part already typed is not undone.
- Text is sent as Unicode characters (layout-independent). Applications that read raw scan codes (some games, remote-desktop clients) may ignore it.
- Keys never available: anything with the Windows key; `Alt+Tab`, `Alt+Esc`, `Ctrl+Esc`, `Ctrl+Shift+Esc`, `Ctrl+Alt+Del`.

## Elevation, sessions and desktops

- WinMCP runs at the user's normal integrity level. **Windows' UIPI blocks UI Automation and input to elevated (administrator) applications**; such processes show `elevated: null` or `true`. Running WinMCP itself elevated is possible but not recommended. A clear `ACCESS_DENIED_ELEVATED` error is planned.
- The secure desktop (UAC prompts, lock screen) is never accessible, by Windows design and by WinMCP's deny-list.
- GUI tests need an unlocked, interactive desktop. GitHub's hosted Windows runners provide one: the whole GUI suite, MFC included, ran there (2026-10-01; the only failure was a test assuming a non-elevated session — runners run as administrator with UAC off). The CI job stays marked experimental until it has been green for a while.

## Display scaling

- WinMCP reports all geometry in **physical pixels** (Per-Monitor-V2, independent of the host process's own DPI awareness); `dpi` is reported per window. Verified at 100% and 150% on a single monitor. **Mixed-DPI multi-monitor setups are untested.**
- Applications that aren't DPI-aware are bitmap-stretched by Windows; their UIA bounds and captures reflect what's on screen, but text in captures may look blurry.
- Coordinates on multi-monitor setups can be negative (virtual-screen space).

## Frameworks

- Verified: WinForms, classic Win32 dialogs, and MFC (dialog app with standard and common controls; CFrameWnd with toolbar, status bar and view — `samples/WinMcp.MfcTestApp`). MFC-specific knowledge (runtime classes, dialog templates) is not used yet (M11); a CView's own drawing is opaque like any custom-drawn control. WPF, WinUI, Qt and Chromium/Electron apps work to the extent their UIA providers do; they have not been tested.

## Not implemented (by design, for now)

Event/message recording, launching or closing applications, coordinate-based clicking, full-desktop screenshots, HTTP transport, reading debugging information (logs, ETW, crash dumps).
