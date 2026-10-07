namespace WinMcp.Core.Desktop;

/// <summary>Raw per-window facts from <see cref="IDesktop"/>; <see cref="WindowQuery.Inspect"/> turns them into a <see cref="WindowInspection"/>.</summary>
/// <param name="Parent">Null for top-level windows.</param>
/// <param name="Style">Raw <c>GWL_STYLE</c>.</param>
/// <param name="ExStyle">Raw <c>GWL_EXSTYLE</c>.</param>
/// <param name="Responding">False when Windows considers the window hung (no message processing for ~5 s).</param>
/// <param name="ChildClassNames">Class name of every descendant window (child HWND), in enumeration order.</param>
public sealed record WindowDetails(
    WindowInfo Window,
    WindowHandle? Parent,
    uint Style,
    uint ExStyle,
    bool Responding,
    int ThreadId,
    IReadOnlyList<string> ChildClassNames);

public sealed record WindowSummary(WindowHandle Hwnd, string Title, string ClassName);

/// <param name="ByClass">Most common descendant window classes, most frequent first (capped).</param>
public sealed record ChildWindowSummary(int Count, IReadOnlyDictionary<string, int> ByClass);

/// <summary>Agent-facing result of <c>inspect_window</c>.</summary>
/// <param name="FrameworkHint">Heuristic from window class names; null when unrecognized.</param>
/// <param name="OwnedWindows">Shown top-level windows owned by this one, typically dialogs.</param>
/// <param name="Framework"><see cref="FrameworkHint"/> with its evidence (loaded modules, window classes), linkage and version.</param>
/// <param name="DialogResources">Dialog templates this window and the dialogs inside it were created from (M11).</param>
public sealed record WindowInspection(
    WindowInfo Window,
    bool Responding,
    int ThreadId,
    IReadOnlyList<string> Styles,
    IReadOnlyList<string> ExtendedStyles,
    string? FrameworkHint,
    IReadOnlyList<WindowSummary> OwnedWindows,
    ChildWindowSummary ChildWindows,
    Native.FrameworkInfo? Framework = null,
    IReadOnlyList<Native.DialogResourceInfo>? DialogResources = null);
