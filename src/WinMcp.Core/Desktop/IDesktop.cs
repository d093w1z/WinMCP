namespace WinMcp.Core.Desktop;

/// <summary>Raw, unfiltered view of the desktop. Implementations must not apply policy; <see cref="WindowQuery"/> does.</summary>
public interface IDesktop
{
    /// <summary>All top-level windows in z-order (topmost first).</summary>
    IReadOnlyList<WindowInfo> GetTopLevelWindows();
}
