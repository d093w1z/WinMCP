using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinMcp.Core.Automation;
using WinMcp.Core.Capture;
using WinMcp.Core.Desktop;

namespace WinMcp.Server.Tools;

/// <summary>Read-only tools, registered in every mode.</summary>
[McpServerToolType]
public sealed class ObserveTools(WindowQuery windows, UiTreeService tree, ScreenshotService screenshots)
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

    [McpServerTool(Name = "get_ui_tree", Title = "Get UI tree", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(UiTree))]
    [Description(
        "The window's UI Automation tree as an indented outline, one element per line: "
        + "[ref] ControlType \"name\" #automation_id value=\"...\" states. "
        + "Refs (e.g. 'e7') identify elements in other tools and stay the same for the same element. "
        + "Title bars and combo box internals are omitted. If the result is truncated, pass a node's ref as 'element' to expand it.")]
    public async Task<CallToolResult> GetUiTree(
        [Description("Window handle from list_windows. Optional when 'element' is given.")] string? hwnd = null,
        [Description("Ref of an element to use as the root instead of the whole window.")] string? element = null,
        [Description("Maximum depth below the root. Default 10.")] int max_depth = UiTreeService.DefaultMaxDepth,
        [Description("Maximum number of nodes returned. Default 300.")] int max_nodes = UiTreeService.DefaultMaxNodes,
        CancellationToken cancellationToken = default)
    {
        var result = await tree.GetTreeAsync(hwnd, element, max_depth, max_nodes, cancellationToken);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = OutlineRenderer.Render(result) }],
            StructuredContent = JsonSerializer.SerializeToElement(result, WinMcpJson.Options),
        };
    }

    [McpServerTool(Name = "find_elements", Title = "Find elements", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(
        "Finds elements in a window's UI Automation tree matching all given criteria (case-insensitive). "
        + "Cheaper than get_ui_tree when you know what you're looking for. Returns refs usable in other tools.")]
    public Task<ElementMatches> FindElements(
        [Description("Window handle from list_windows. Optional when 'element' is given.")] string? hwnd = null,
        [Description("Ref of an element to search within instead of the whole window.")] string? element = null,
        [Description("Exact AutomationId, e.g. 'applyButton'. For Win32/MFC controls this is the numeric control ID.")] string? automation_id = null,
        [Description("Exact element name (for most controls, their label or caption).")] string? name = null,
        [Description("Substring of the element name.")] string? name_contains = null,
        [Description("UI Automation control type, e.g. Button, Edit, ComboBox, CheckBox, Text, List, ListItem, MenuItem.")] string? control_type = null,
        [Description("Exact window class name, e.g. 'Button' or 'SysListView32'.")] string? class_name = null,
        [Description(ToolText.ControlSymbol)] string? control_symbol = null,
        [Description("Maximum matches returned. Default 25.")] int max_results = UiTreeService.DefaultMaxResults,
        CancellationToken cancellationToken = default) =>
        tree.FindAsync(hwnd, element, new ElementLocator(automation_id, name, name_contains, control_type, class_name, control_symbol), max_results, cancellationToken);

    [McpServerTool(Name = "inspect_element", Title = "Inspect element", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(
        "Full details of one element: value and states, supported UI Automation patterns (what it can do), "
        + "Win32 hwnd and control ID (with its symbolic name: MFC standard IDs always, resource.h names when configured), combo box options (read without opening it), "
        + "bounds, parent and label refs, and a suggested durable 'locator' for finding it again in a later session. "
        + "Identify the element by 'element' (a ref), or by 'hwnd' plus criteria that match exactly one element.")]
    public Task<ElementDetail> InspectElement(
        [Description("Ref from get_ui_tree/find_elements, e.g. 'e7'. With criteria, searches within this element.")] string? element = null,
        [Description("Window handle from list_windows; use with criteria.")] string? hwnd = null,
        [Description("Exact AutomationId.")] string? automation_id = null,
        [Description("Exact element name.")] string? name = null,
        [Description("UI Automation control type, e.g. Button, Edit, ComboBox.")] string? control_type = null,
        [Description(ToolText.ControlSymbol)] string? control_symbol = null,
        CancellationToken cancellationToken = default) =>
        tree.InspectAsync(hwnd, element, new ElementLocator(automation_id, name, null, control_type, null, control_symbol), cancellationToken);

    [McpServerTool(Name = "capture_screenshot", Title = "Capture screenshot", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "PNG image of a window, or of one element (by ref or criteria) with optional padding. "
        + "Use it to check visual layout or content the UI tree doesn't expose (e.g. custom-drawn areas); prefer get_ui_tree for reading text and state. "
        + "Rendered by the window itself, so covered windows still capture correctly and other applications never appear. "
        + "Returns the image plus JSON metadata (source bounds in screen pixels, scale, DPI).")]
    public async Task<CallToolResult> CaptureScreenshot(
        [Description("Window handle from list_windows. Alone, captures the whole window.")] string? hwnd = null,
        [Description("Ref of an element to capture instead of the whole window.")] string? element = null,
        [Description("Exact AutomationId of the element to capture (with hwnd).")] string? automation_id = null,
        [Description("Exact element name (with hwnd).")] string? name = null,
        [Description("UI Automation control type (with hwnd).")] string? control_type = null,
        [Description("Extra pixels around an element (0–200). Default 0.")] int padding = 0,
        [Description("Longest image edge in pixels; larger captures are scaled down (64–4096). Default 1280.")] int max_edge = ScreenshotService.DefaultMaxEdge,
        CancellationToken cancellationToken = default)
    {
        var shot = await screenshots.CaptureAsync(hwnd, element, new ElementLocator(automation_id, name, null, control_type), padding, max_edge, cancellationToken);
        return new CallToolResult
        {
            Content =
            [
                ImageContentBlock.FromBytes(shot.Png, "image/png"),
                new TextContentBlock { Text = JsonSerializer.Serialize(shot.Info, WinMcpJson.Options) },
            ],
        };
    }

    [McpServerTool(Name = "wait_for", Title = "Wait for", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(
        "Waits until a condition holds, polling the UI: 'exists' / 'gone' (any element matching the criteria), "
        + "'enabled', 'text_equals', 'text_contains' (exactly one element; text is its value, or its name if it has no value — e.g. a status label). "
        + "Use after an action whose effect is not immediate. Returns TIMEOUT (retryable) with the last observed text if it doesn't happen.")]
    public Task<WaitResult> WaitFor(
        [Description("exists | gone | enabled | text_equals | text_contains")] string condition,
        [Description("Expected text for text_equals/text_contains (case-sensitive).")] string? text = null,
        [Description("Ref of the element to watch.")] string? element = null,
        [Description("Window handle from list_windows; use with criteria.")] string? hwnd = null,
        [Description("Exact AutomationId.")] string? automation_id = null,
        [Description("Exact element name.")] string? name = null,
        [Description("UI Automation control type.")] string? control_type = null,
        [Description(ToolText.ControlSymbol)] string? control_symbol = null,
        [Description("Maximum wait in milliseconds (0–60000). Default 5000.")] int timeout_ms = UiTreeService.DefaultWaitTimeoutMs,
        CancellationToken cancellationToken = default) =>
        tree.WaitAsync(hwnd, element, new ElementLocator(automation_id, name, null, control_type, null, control_symbol),
            ActionArguments.ParseCondition(condition), text, timeout_ms, cancellationToken);
}
