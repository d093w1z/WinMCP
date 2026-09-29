using WinMcp.Core.Errors;
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

public sealed class WindowQuery(IDesktop desktop, TargetPolicy policy)
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

    private string HintForEmpty() => policy.AllowList.Count == 0
        ? "No applications are allowlisted, so no windows are visible. The user must start WinMCP with --allow <process-name>."
        : $"No matching windows. Only these applications are visible: {string.Join(", ", policy.AllowList)}. "
          + "If the target is running but not listed, it is not allowlisted; if it is minimized to the tray or hidden, try include_hidden.";
}
