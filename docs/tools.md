# WinMCP tool reference

WinMCP exposes 12 MCP tools over stdio. **Observe** tools are always available; **control** tools exist only when the server runs with `--mode control` (in observe mode they are absent from `tools/list`, not merely refused).

All results are JSON with `snake_case` names, returned as `structuredContent` (plus the same JSON as text), except `get_ui_tree` (text is a compact outline) and `capture_screenshot` (an image block plus JSON text).

**Typical flow:** `list_windows` → `get_ui_tree` (or `find_elements`) → act by ref (`invoke`, `set_value`, …) → verify with `wait_for`.

## Identifying things

| Thing | Form | Lifetime |
|-------|------|----------|
| Window | `hwnd:0x000A0B1C` from `list_windows` | Until the window closes |
| Element ref | `e7` from `get_ui_tree` / `find_elements` / `inspect_element` | The server session; the same element always gets the same ref. A ref to a removed element returns `ELEMENT_STALE`; refs never point to a different element |
| Locator | criteria such as `automation_id`, `name`, `control_type`, `control_symbol` (with `hwnd`) | Durable across sessions; must match **exactly one** element for actions (`AMBIGUOUS_MATCH` lists candidate refs otherwise) |

Element-taking tools accept either `element` (a ref), or `hwnd` plus criteria. `inspect_element` returns a suggested durable `locator` for each element, checked to be unique.

## Observe tools

### `list_windows`
Top-level windows of **allowlisted** applications only. Parameters: `process_name`, `title_contains`, `pid`, `include_hidden`.
Returns `windows[]` (`hwnd`, `title`, `class_name`, `process` {`pid`, `name`, `path`, `architecture`, `elevated`}, `visible`, `cloaked`, `enabled`, `minimized`, `maximized`, `foreground`, `bounds`, `dpi`, `owner`), `excluded_count` (windows hidden by policy, counted before your filters), `hint` when empty.

### `inspect_window`
One top-level window: `window`, `responding`, decoded `styles` / `extended_styles` (`WS_CAPTION`, `WS_EX_TOPMOST`, …), `owned_windows` (non-empty usually means a modal dialog is open), `child_windows` {`count`, `by_class`}, `framework_hint` (`winforms`, `wpf`, `mfc`, `win32-dialog`, `chromium`, `qt`, …).

### `get_ui_tree`
The UI Automation tree as an outline, one element per line:

```text
[e1] Window "WinMCP Test App" #MainForm
  [e3] Edit "Name:" #nameTextBox value=""
  [e5] ComboBox "Type:" #typeComboBox value="Text" collapsed
  [e8] Button "Advanced..." #advancedButton disabled
  [e11] Text "Status: Ready" #statusLabel
(12 nodes)
```

Parameters: `hwnd` or `element` (subtree root), `max_depth` (default 10), `max_nodes` (default 300). Title bars and combo-box internals are omitted; anonymous single-child panes are collapsed. When truncated, lines like `… 12 more` appear — pass that node's ref as `element`. Win32/MFC numeric ids show their `resource.h` name when configured: `#1000 (IDC_EDIT_NAME)`. States: `disabled`, `offscreen`, `focused`, `password`, `collapsed`/`expanded`, `on`/`off`/`indeterminate`, `selected`. Password values are never returned.

### `find_elements`
Elements matching all given criteria (case-insensitive): `automation_id`, `name`, `name_contains`, `control_type` (UIA names — `Edit`, not `TextBox`; common mistakes get a correction hint), `class_name`, `control_symbol`. Scope with `hwnd` or `element`; `max_results` (default 25). Returns `matches[]` with refs, `count`, `truncated`.

### `inspect_element`
Full detail of one element: value and states, `class_name`, `framework_id`, own `hwnd`, `host_hwnd`, Win32 `control_id` and `control_symbol` (or `control_symbol_candidates` when several names share the id), `patterns` (what it can do: `Invoke`, `Value`, `Toggle`, …), `focusable`, `options` + `option_count` for combo boxes (read **without** opening them), `bounds`, `parent`, `labeled_by`, `help_text`, and a suggested `locator` {…, `unique`}.

### `wait_for`
Polls until a condition holds: `exists` / `gone` (any element matching the criteria), `enabled`, `text_equals` / `text_contains` (exactly one element; text = its value, or its name if it has none, e.g. a status label). `timeout_ms` 0–60000 (default 5000). Timeout → `TIMEOUT` (retryable) with `last_text`.

```json
{"name":"wait_for","arguments":{"hwnd":"hwnd:0x000A0B1C","automation_id":"statusLabel","condition":"text_contains","text":"Applied"}}
```

### `capture_screenshot`
PNG of a window, or of one element (`element` or criteria, with `padding`). The window renders itself, so covered windows capture correctly and **other applications never appear**. `max_edge` (default 1280) scales large captures down. Returns an image block plus `{window, element, source_bounds, image_width, image_height, scale, dpi, method}`. Prefer `get_ui_tree` for reading text and state.

## Control tools (`--mode control`)

All control tools refuse disabled elements (`ELEMENT_DISABLED`) — Windows' own UI Automation would otherwise click disabled WinForms buttons and run their handlers — and every call is written to the audit log. Results: `{ok, action, element, method, changed, value_after?, state_after?, warning?, elapsed_ms}`; `method` says how it was done (`uia.InvokePattern`, `win32.CB_SETCURSEL+CBN_SELCHANGE`, …, or `none` when nothing had to change).

| Tool | Does | Notes |
|------|------|-------|
| `invoke` | Click: button press, check-box toggle, item select, expand/collapse | Destructive annotation. If the handler blocks (e.g. opens a modal dialog), returns success with a `warning` instead of a timeout — don't click again |
| `set_value` | Replace a field's text (`value`) | Password fields refused; read-only values refused; `value_after` is read back |
| `select_option` | Select an item by text (`option`) in a combo box, list or tab control — target the container | Opens a collapsed combo only as long as needed and closes it again; unknown option → `OPTION_NOT_FOUND` with `available` |
| `set_toggle` | Set a check box to `state` `on`/`off` | Target state, so repeating is harmless (`changed: false`) |
| `send_keys` | Type `text` literally, or `keys` as chords: `"Ctrl+A, Backspace, Enter"` | Optional `element` to focus first. The window is brought to the foreground and input stops with `FOCUS_FAILED` if anything else takes it. Windows-key and window-switching chords refused; password fields refused |

Golden scenario:

```json
{"name":"set_value","arguments":{"hwnd":"hwnd:0x000A0B1C","automation_id":"nameTextBox","value":"Mukesh"}}
{"name":"select_option","arguments":{"hwnd":"hwnd:0x000A0B1C","automation_id":"typeComboBox","option":"HTML"}}
{"name":"invoke","arguments":{"hwnd":"hwnd:0x000A0B1C","automation_id":"applyButton"}}
{"name":"wait_for","arguments":{"hwnd":"hwnd:0x000A0B1C","automation_id":"statusLabel","condition":"text_contains","text":"Applied"}}
```

## Errors

Tool failures are results with `isError: true` and

```json
{"error":{"code":"OPTION_NOT_FOUND","message":"\"Type:\" has no option 'PDF'.","category":"caller","retryable":false,
          "hint":"Use one of the available options.","details":{"available":["Text","HTML","Markdown"]}}}
```

| Category | Meaning | Codes |
|----------|---------|-------|
| `caller` | Change the arguments | `INVALID_ARGUMENT`, `WINDOW_NOT_FOUND`, `ELEMENT_NOT_FOUND`, `AMBIGUOUS_MATCH`, `OPTION_NOT_FOUND`, `PATTERN_NOT_SUPPORTED`, `ELEMENT_DISABLED`, `ELEMENT_STALE` |
| `policy` | Refused by WinMCP's rules | `TARGET_NOT_ALLOWED`, `OPERATION_NOT_PERMITTED`, `PASSWORD_FIELD` |
| `environment` | The application or desktop state | `ACCESS_DENIED_ELEVATED`, `TARGET_NOT_RESPONDING`, `WINDOW_MINIMIZED`, `ELEMENT_OFFSCREEN`, `WINDOW_CLOSED`, `FOCUS_FAILED`, `TIMEOUT` |
| `internal` | A WinMCP bug (details in the server log, never in the result) | `INTERNAL_ERROR` |

`retryable: true` means the **same** call may succeed later unchanged: only `TARGET_NOT_RESPONDING`, `FOCUS_FAILED`, `TIMEOUT`. Windows of non-allowlisted applications are reported as `WINDOW_NOT_FOUND`, indistinguishable from windows that don't exist.
