# MFC investigation (M11)

What WinMCP can learn about MFC applications beyond what UI Automation shows, measured on Windows 11 26100 (2026-10-01). The plan (mvp-plan.md, "M11 — MFC investigation plan") listed six things to try, from cheap and out-of-process to invasive. This report gives the result of each, what shipped, and the go/no-go on an in-process inspector.

## Verdict

| # | Idea | Result | Shipped |
|---|------|--------|---------|
| 1 | Detect MFC | **Reliable.** Shared MFC: the loaded `mfc140u.dll`, with its version. Static MFC: `Afx…140su` window classes, or MFC's class names inside the executable (the only sign for a static *dialog* app). | `inspect_window.framework` {name, linkage, version, evidence} |
| 2 | HWND → likely MFC class | **Reliable for the base class, never for the application's class.** Standard IDs (`AFX_IDW_*`) make frame parts certain. | `inspect_element.mfc_class_guess` |
| 3 | Dialog templates | **Reliable.** Live dialogs match their `RT_DIALOG` template by control IDs (score 1.0 for untouched dialogs); differences show run-time changes. Works for any Win32 app (charmap, via its MUI file). | `inspect_window.dialog_resources` |
| 4 | Symbolic IDs | **Reliable.** MFC's `afxres.h` IDs are known for every MFC process without configuration; `IDD_*` names come from a configured `resource.h`; menu items get command symbols. | outline, `find_elements`, `inspect_element`, locators |
| 5 | UIA quality per control type | Standard and common controls: good. Feature Pack: mixed — the property grid is opaque. | Documented in limitations.md; regression tests |
| 6 | In-process runtime class | **Works, exactly as predicted, with every predicted constraint.** Adds the application's own class names and exact Feature Pack classes; nothing else an agent needs. | **No-go for the server.** Research code in `spikes/M11.MfcRuntimeClass` |

## Test subjects

- `samples/WinMcp.MfcTestApp` in three modes — dialog (the TestApp contract), `--frame` (CFrameWnd + CView + CToolBar + CStatusBar), `--features` (owner-drawn button, CMFCButton, CMFCColorButton, CMFCEditBrowseCtrl, CMFCMaskedEdit, CMFCPropertyGridCtrl) — each built with **shared and static MFC** (`scripts/build-mfc.ps1` builds both).
- `charmap.exe`: a real Win32 dialog application whose resources live in a MUI file.
- Real-world MFC: Spy++ (`spyxx_amd64.exe`, shared MFC 14) is the only MFC application on the test machine, and it **requires elevation** (its manifest asks for administrator rights), so it is out of reach for WinMCP by design. A real-world MFC application should be tried in the development environment.

## 1. Detecting MFC

| Build | Signal | Result |
|---|---|---|
| Shared MFC, any mode | `mfc140u.dll` in the module list (`EnumProcessModulesEx`) | `mfc`, `shared`, version `14.51.36247.0` (the redistributable that is installed, not the build toolset's) |
| Static, frame | window class `AfxFrameOrView140su` (the `s` marks static builds) | `mfc`, `static` |
| Static, Feature Pack | window class `Afx:PropList:…` | `mfc`, `static` |
| Static, dialog | **no MFC-specific window class at all** — it looked exactly like `win32-dialog` | fixed: the executable contains `CCmdTarget`/`CWinThread` (names of MFC's runtime-class objects, which shared-MFC apps keep in `mfc140u.dll`) → `mfc`, `static` |
| WinForms TestApp, charmap | — | unchanged (`winforms`, `win32-dialog`); the executable scan runs only when nothing else decided and is cached per file |

Cost: the module list takes a few milliseconds and is cached for 30 s per process; `inspect_window` measured 40–120 ms including template matching. When the process can't be opened (elevated), detection falls back to window classes.

## 2. Likely MFC class

Mapping window class + control ID to the MFC wrapper: `Button` → `CButton`, `SysListView32` → `CListCtrl`, `#32770` → `CDialog` (top level) / `CFormView` (child with a pane ID) / `CDialogBar`, `ToolbarWindow32` with `AFX_IDW_TOOLBAR` → `CToolBar`, `AfxFrameOrView140u` → `CFrameWnd` (top level) / `CView` (pane ID), `Afx:PropList` → `CMFCPropertyGridCtrl`, and so on.

Compared with the truth from step 6:

| Window | Guess | Actual runtime class |
|---|---|---|
| Main dialog | `CDialog` | `CDialogEx` (the app's `CMainDlg` has no `DECLARE_DYNAMIC`) |
| Features dialog | `CDialog` | `CFeaturesDlg : CDialogEx` |
| Frame, view, toolbar, status bar | `CFrameWnd`, `CView`, `CToolBar`, `CStatusBar` | same |
| Combo, list, tree | `CComboBox`, `CListCtrl`, `CTreeCtrl` | same |
| Name edit, buttons, statics | `CEdit`, `CButton`, `CStatic` | **no C++ object** — plain template controls |
| Color button, masked edit | `CButton`, `CEdit` | `CMFCColorButton`, `CMFCMaskedEdit` |

So the guess names the right MFC *family* and is exact for frame parts, but can't know application classes, Feature Pack subclasses of standard controls, or whether a C++ object exists at all. It is labelled `mfc_class_guess` accordingly.

## 3. Dialog templates

`RT_DIALOG` resources are read with `LoadLibraryEx(LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE)` — no code of the module runs — from the executable, application DLLs loaded from its folder, and `<exe folder>\<LCID>\*.dll` (the MFC satellite-DLL convention, e.g. Spy++'s `1033\spyxxui_amd64.dll`; such DLLs are often loaded as data files and don't show in the module list). `EnumResourceNamesEx` with `RESOURCE_ENUM_MUI` also finds templates in MUI files. The `DLGTEMPLATE`/`DLGTEMPLATEEX` parser is pure C# and unit-tested.

Matching uses the Jaccard similarity of significant control IDs (not `IDC_STATIC`, not 0); captions only break ties, since applications change them.

| Dialog | Result |
|---|---|
| MFC main dialog | `IDD_MAIN` #100, match 1.0; `IDC_EDIT_HIDDEN` reported as hidden |
| After "Add field" | still `IDD_MAIN`; `IDC_EDIT_DYNAMIC1` and `IDC_STATIC_DYNAMIC1` reported as extra (created at run time) |
| Modal Confirm dialog | `IDD_CONFIRM` #101, 1.0 |
| Features dialog | `IDD_FEATURES` #104, 1.0 (the property grid replaced a placeholder with the same ID) |
| charmap | #13002 "Character Map", 0.86: the character grid, scroll bar and status bar are created in code (extra), the advanced-view controls hidden; three equally good templates (#13003, #13011, #13012) are listed as alternatives rather than guessed |

Two corrections came out of this: hidden controls first counted as "missing" (the Win32 child list skipped invisible windows) and charmap scored below the threshold because of it.

## 4. Symbolic IDs

- MFC's standard IDs are generated from `afxres.h` (MSVC 14.44): 16 frame window IDs (`AFX_IDW_TOOLBAR`, `AFX_IDW_STATUS_BAR`, `AFX_IDW_PANE_FIRST`, dock bars, …) and 93 commands (`ID_FILE_NEW`, `ID_APP_EXIT`, …). `AFX_IDC_*` is left out: mostly cursors, and small numbers (100, 1000) that collide with application control IDs.
- They apply to MFC processes only, and the application's own `resource.h` names win for any ID they define.
- Menu items of Win32/MFC apps expose their command ID as automation id (`MenuItem "Exit" #57665`), so they get command symbols (`ID_*`/`IDM_*`), and `control_symbol: "ID_APP_EXIT"` locates them.
- With `resource.h`, dialog templates get their `IDD_*` names.

Measured on the frame without any configuration: `Pane #59648 (AFX_IDW_PANE_FIRST)`, `ToolBar #59392 (AFX_IDW_TOOLBAR)`, `StatusBar #59393 (AFX_IDW_STATUS_BAR)`, `MenuItem "Exit" #57665 (ID_APP_EXIT)`.

A bug found here: `inspect_element` looked up symbols by the control ID alone, so the property grid's internal header (ID 1) was called `IDOK`. Control IDs are unique among siblings only; symbols are now given only where UI Automation exposes the control ID as automation id (dialog controls, menu commands).

## 5. UI Automation quality

| Control | What UIA shows | Operable |
|---|---|---|
| Edit, combo, check box, push button, list, tree, tabs | as for Win32 (M10) | yes |
| Owner-drawn button | `Button` named by its **window text** ("Owner drawn"), not the painted text ("Draw me") | `invoke` (BM_CLICK) |
| CMFCButton | `Button` | `invoke` |
| CMFCColorButton | `Button ""` — no name, no value; the color is invisible | clicking opens a popup (not tested further) |
| CMFCEditBrowseCtrl | `Edit`; the browse button (non-client area) isn't exposed | `set_value` |
| CMFCMaskedEdit | `Edit`, value **without literals** (`5551234567`) | `set_value` only in display format (`(555) 123-4567`); raw digits are rejected by the control and reported as `changed: false` |
| CMFCPropertyGridCtrl | `List "PropertyList"` with only its header; **no rows**, not keyboard-focusable per UIA | no — screenshot to read |
| CView content, custom canvas | empty `Pane` | no (screenshot) |
| Toolbar buttons | `Button` named from the tooltip part of the `"prompt\ntip"` string | `invoke` (focus restored afterwards, M10) |
| Status bar panes | `Text` elements with the pane text | read-only |
| MFC's own error box ("Encountered an improper argument.") | a normal Win32 message box | readable, closable |

Two general problems surfaced and were fixed:

- **Native menu bar items of an inactive window have no UIA runtime id.** All of them shared one element key, so they got one ref, and an action could reach the wrong item. Elements without a runtime id now get an identity from their parent's id, their position and their name, computed the same way by tree fetch and live lookup.
- **Native menus open only in the active window.** UIA's Expand/Invoke on a menu bar item of a background window failed with "operation is not valid due to the current state" (an `INTERNAL_ERROR`). WinMCP now activates the window first, as a click would, and reports `FOCUS_FAILED` — with a locked-desktop hint when no window is active at all — if Windows refuses. (WinForms menus are unaffected: they open in the background.)

## 6. In-process runtime class (spike)

`spikes/M11.MfcRuntimeClass`: `Injector.exe <hwnd>` installs a **thread-scoped** `WH_GETMESSAGE` hook on the window's UI thread with `ProbeHook.dll`, an MFC *extension* DLL (no module state of its own, so on that thread it sees the application's handle map). The hook calls `CWnd::FromHandlePermanent` for every window of the thread and follows `GetRuntimeClass()` → `m_pfnGetBaseClass`.

Results (round trip ~170 ms including hook installation):

- Shared-MFC dialog: `#32770 → CDialogEx : CDialog : CWnd : CCmdTarget : CObject`; `CComboBox`, `CListCtrl`, `CTreeCtrl` for the controls the dialog wraps; the custom canvas `CWnd` (its class has no `DECLARE_DYNAMIC`); **every other control: no permanent CWnd** — MFC only knows windows it created or subclassed.
- Features dialog (with `DECLARE_DYNAMIC`): `CFeaturesDlg : CDialogEx : …`, `CMFCColorButton : CMFCButton : CButton`, `CMFCMaskedEdit : CEdit`, `CMFCEditBrowseCtrl : CEdit`, `CMFCPropertyGridCtrl`, its `CMFCHeaderCtrl`.
- Frame: `CFrameWnd`, `CView`, `CToolBar : CControlBar`, `CStatusBar : CControlBar`.
- **Static MFC: nothing at all** — the DLL brings its own copy of MFC, whose handle map is empty; the application's map is private to its executable.

Constraints confirmed or implied: same MFC DLL as the target (shared builds only), matching bitness (x64 helper for x64 targets; x86 needs a second build), per-thread handle maps (must run on the window's thread), blocked by UIPI for elevated targets, and it **loads code into another process** — exactly what security tools flag, and outside WinMCP's security design ("no unrestricted remote access", read-only observation by default).

**Decision: no-go for WinMCP.** What it adds over steps 1–4 is the application's own class names (only where declared with `DECLARE_DYNAMIC`) and exact Feature Pack subclasses — useful when debugging one's own application, but no help for an agent deciding what to click, and not worth code injection in a tool meant to be safe by default. If "debug my own app" becomes a goal, the better route is cooperative: an optional helper the developer links into their own application that answers the same question over a local channel, instead of injection into arbitrary processes.

## Next steps

- Try a real-world, non-elevated MFC application in the development environment (Spy++ can't be used; many line-of-business MFC apps can), especially one with satellite resource DLLs and Feature Pack UI.
- Property grid and other opaque Feature Pack controls: whether the focused property becomes visible through MSAA when the grid has keyboard focus (untested here: the desktop refused focus changes during that run) would be the next thing to measure if such apps matter.
- x86 targets: detection and templates are bitness-independent (data only); nothing else in M11 needs changes, but it hasn't been tested.
