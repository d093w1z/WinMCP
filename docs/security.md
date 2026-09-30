# Security model

WinMCP lets an AI agent read and operate desktop applications as the signed-in user. Treat it like any tool that can type and click on your behalf: give it access to the applications it needs and nothing else.

## Boundaries

| Control | What it does | Default |
|---------|--------------|---------|
| **Transport** | stdio only: the MCP client starts WinMCP as a child process. No network listener exists. | — |
| **Mode** (`--mode`) | `observe`: read-only tools only. `control`: adds `invoke`, `set_value`, `select_option`, `set_toggle`, `send_keys`. In observe mode control tools are **not registered**, so a client can't even see them. | `observe` |
| **Allowlist** (`--allow`) | Process names or full executable paths. Only windows of these processes are ever visible or actionable. `--mode control` refuses to start without one. | empty: **nothing** is visible |
| **Deny-list** | UAC (`consent`), `LogonUI`, `winlogon`, `CredentialUIBroker`, and WinMCP itself can never be targeted, even if allowlisted. | always on |
| **Audit log** | Every control action — successful or refused — is appended as JSON to `%LOCALAPPDATA%\WinMCP\audit\audit-YYYYMMDD.jsonl` (tool, window, process, element, arguments, outcome, method, duration). | always on (`--audit-dir` to relocate) |

Configuration comes only from the command line the user (or their MCP client config) supplies. No tool can change the mode, the allowlist, or read files (`resource.h` paths for `--symbols` are operator configuration, never tool arguments).

## What an agent can never learn about other applications

- `list_windows` applies the allowlist **before** the caller's filters, so filters such as `title_contains: "password"` can't probe other applications. Only an aggregate `excluded_count` is returned.
- A window handle of a non-allowlisted application gets the same `WINDOW_NOT_FOUND` as a handle that doesn't exist.
- Screenshots are rendered by the target window itself (`PrintWindow`), so pixels of overlapping windows never appear. There is intentionally no screen-copy fallback.
- Every action re-checks the allowlist when it runs (handles and process ids can be reused).

## Protections inside allowlisted applications

- **Password fields**: values are never returned (trees, `inspect_element`, `wait_for` text conditions are refused on them); `set_value` and `send_keys` refuse to type into them; audit entries redact their values.
- **Disabled controls** are refused before any input reaches the application. (Windows' UI Automation would otherwise click a disabled WinForms button and run its handler.)
- **`send_keys`** only types while the target window is the foreground window, re-checking before every chunk, and stops with `FOCUS_FAILED` rather than type into anything else. Windows-key and window-switching chords (`Alt+Tab`, `Ctrl+Esc`, …) are refused because later input would reach another application.
- **Ambiguity is an error**: an action whose criteria match several elements is refused with the candidates listed; WinMCP never guesses a target.

## Prompt injection

Text shown inside applications (window titles, labels, document content) is returned to the agent as data. The server's MCP instructions tell the agent that such text is never an instruction to follow, but that is advisory: an agent can still be misled by what an application displays. Keep the allowlist narrow and prefer `observe` mode where interaction isn't needed.

## What is out of scope

- WinMCP runs with the user's own rights. It doesn't elevate, and Windows prevents it from controlling elevated applications (UIPI). Running WinMCP elevated would lift that barrier — don't, unless you understand the consequence.
- Tool annotations (`readOnlyHint`, `destructiveHint`, …) are hints for MCP clients' confirmation prompts, not enforcement.
- The audit log is local and not tamper-proof; it records what WinMCP did, for troubleshooting and review.
