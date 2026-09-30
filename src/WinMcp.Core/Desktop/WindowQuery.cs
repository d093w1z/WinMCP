using WinMcp.Core.Errors;
using WinMcp.Core.Native;
using WinMcp.Core.Policy;

namespace WinMcp.Core.Desktop;

/// <param name="ProcessName">Exact, case-insensitive; <c>.exe</c> optional.</param>
/// <param name="TitleContains">Case-insensitive substring.</param>
public sealed record WindowFilter(string? ProcessName = null, string? TitleContains = null, int? Pid = null, bool IncludeHidden = false);

/// <param name="ExcludedCount">
/// Windows hidden by policy. Counted before the caller's filters so filters can't be used to probe
/// titles or names of non-allowlisted applications.
/// </param>
public sealed record WindowList(IReadOnlyList<WindowInfo> Windows, int ExcludedCount, string? Hint);

/// <param name="native">Framework and dialog-template facts (M11); without it, inspection uses window class names only.</param>
public sealed class WindowQuery(IDesktop desktop, TargetPolicy policy, NativeAppInfo? native = null)
{
    public WindowList List(WindowFilter filter)
    {
        if (filter.Pid is <= 0)
            throw new WinMcpException(new WinMcpError(WinMcpErrorCode.InvalidArgument, $"pid must be positive, got {filter.Pid}."));

        var processName = filter.ProcessName?.Trim() is { Length: > 0 } name
            ? (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name)
            : null;

        var candidates = desktop.GetTopLevelWindows().Where(w => filter.IncludeHidden || w.Shown).ToList();
        var allowed = candidates.Where(w => policy.IsAllowed(w.Process)).ToList();
        var excluded = candidates.Count - allowed.Count;

        var matches = allowed
            .Where(w => processName is null || string.Equals(w.Process.Name, processName, StringComparison.OrdinalIgnoreCase))
            .Where(w => filter.TitleContains is null || w.Title.Contains(filter.TitleContains, StringComparison.OrdinalIgnoreCase))
            .Where(w => filter.Pid is null || w.Process.Pid == filter.Pid)
            .ToList();

        return new WindowList(matches, excluded, matches.Count == 0 ? HintForEmpty() : null);
    }

    public const int MaxChildClassesReported = 15;

    /// <summary>
    /// Details of one top-level window. A window of a non-allowlisted application yields the same
    /// WINDOW_NOT_FOUND as a nonexistent handle, so the tool can't be used to confirm what else is running.
    /// </summary>
    public WindowInspection Inspect(string? hwnd)
    {
        var details = ResolveTopLevel(hwnd);
        var handle = details.Window.Hwnd;

        var owned = desktop.GetTopLevelWindows()
            .Where(w => w.Owner == handle && w.Shown && policy.IsAllowed(w.Process))
            .Select(w => new WindowSummary(w.Hwnd, w.Title, w.ClassName))
            .ToList();

        var byClass = details.ChildClassNames
            .GroupBy(c => c)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(MaxChildClassesReported)
            .ToDictionary(g => g.Key, g => g.Count());

        var framework = native?.Framework(details)
                        ?? FrameworkDetection.Detect(details.Window.ClassName, details.ChildClassNames, modules: null, _ => null);

        return new WindowInspection(
            details.Window,
            details.Responding,
            details.ThreadId,
            WindowStyles.DecodeStyle(details.Style),
            WindowStyles.DecodeExStyle(details.ExStyle),
            framework?.Name,
            owned,
            new ChildWindowSummary(details.ChildClassNames.Count, byClass),
            framework,
            native?.Dialogs(details, desktop.GetChildWindows(handle, includeHidden: true)) ?? []);
    }

    /// <summary>Whether an allowlisted window is processing messages right now.</summary>
    public bool AnswersMessages(WindowHandle window)
    {
        ResolveTopLevel(window.ToString());
        return desktop.AnswersMessages(window);
    }

    /// <summary>Child windows of an allowlisted top-level window (gatekept like everything else).</summary>
    public IReadOnlyList<ChildWindow> ChildWindows(WindowHandle window)
    {
        ResolveTopLevel(window.ToString());
        return desktop.GetChildWindows(window);
    }

    /// <summary>
    /// The single gate for every tool that takes a window handle: well-formed, existing, allowlisted, top-level.
    /// A non-allowlisted window yields the same WINDOW_NOT_FOUND as a nonexistent one.
    /// </summary>
    public WindowDetails ResolveTopLevel(string? hwnd)
    {
        if (!WindowHandle.TryParse(hwnd, out var handle))
            throw new WinMcpException(new WinMcpError(
                WinMcpErrorCode.InvalidArgument,
                $"'{hwnd}' is not a window handle.",
                Hint: "Use an 'hwnd' value returned by list_windows, e.g. 'hwnd:0x000A0B1C'."));

        var details = desktop.GetWindowDetails(handle);
        if (details is null || !policy.IsAllowed(details.Window.Process))
            throw new WinMcpException(new WinMcpError(
                WinMcpErrorCode.WindowNotFound,
                $"No accessible window {handle}.",
                Hint: "The window may have closed. Call list_windows for current handles."));

        if (details.Parent is not null)
            throw new WinMcpException(new WinMcpError(
                WinMcpErrorCode.InvalidArgument,
                $"{handle} is a child window (a control), not a top-level window.",
                Hint: "Pass a top-level window from list_windows."));

        return details;
    }

    private string HintForEmpty() => policy.AllowList.Count == 0
        ? "No applications are allowlisted, so no windows are visible. The user must start WinMCP with --allow <process-name>."
        : $"No matching windows. Only these applications are visible: {string.Join(", ", policy.AllowList)}. "
          + "If the target is running but not listed, it is not allowlisted; if it is minimized to the tray or hidden, try include_hidden.";
}
