namespace WinMcp.Core.Desktop;

/// <summary>Raw, unfiltered view of the desktop. Implementations must not apply policy; <see cref="WindowQuery"/> does.</summary>
public interface IDesktop
{
    /// <summary>All top-level windows in z-order (topmost first).</summary>
    IReadOnlyList<WindowInfo> GetTopLevelWindows();

    /// <summary>Details of any window (top-level or child); null when the handle doesn't identify an existing window.</summary>
    WindowDetails? GetWindowDetails(WindowHandle hwnd);
}
