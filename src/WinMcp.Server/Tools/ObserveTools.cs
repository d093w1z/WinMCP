using System.ComponentModel;
using ModelContextProtocol.Server;
using WinMcp.Core.Desktop;

namespace WinMcp.Server.Tools;

/// <summary>Read-only tools, registered in every mode.</summary>
[McpServerToolType]
public sealed class ObserveTools(WindowQuery windows)
{
    [McpServerTool(Name = "list_windows", Title = "List windows", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(
        "Lists top-level windows of the applications WinMCP is allowed to access (other applications are never shown). "
        + "Start here: the returned 'hwnd' identifies a window in other tools. Hidden and cloaked windows are omitted unless include_hidden is true.")]
    public WindowList ListWindows(
        [Description("Exact process name, case-insensitive, '.exe' optional. Example: 'WinMcp.TestApp'.")] string? process_name = null,
        [Description("Case-insensitive substring of the window title.")] string? title_contains = null,
        [Description("Process ID.")] int? pid = null,
        [Description("Also return hidden and cloaked windows. Default false.")] bool include_hidden = false) =>
        windows.List(new WindowFilter(process_name, title_contains, pid, include_hidden));

    [McpServerTool(Name = "inspect_window", Title = "Inspect window", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(
        "Detailed information about one top-level window: whether it is responding, decoded Win32 styles, "
        + "dialogs it owns (owned_windows — a non-empty list usually means a modal dialog is open), "
        + "a summary of its child windows by class, and a heuristic UI framework hint.")]
    public WindowInspection InspectWindow(
        [Description("Window handle from list_windows, e.g. 'hwnd:0x000A0B1C'.")] string hwnd) =>
        windows.Inspect(hwnd);
}
