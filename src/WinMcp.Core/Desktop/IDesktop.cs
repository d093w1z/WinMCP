namespace WinMcp.Core.Desktop;

/// <summary>Raw, unfiltered view of the desktop. Implementations must not apply policy; <see cref="WindowQuery"/> does.</summary>
public interface IDesktop
{
    /// <summary>All top-level windows in z-order (topmost first).</summary>
    IReadOnlyList<WindowInfo> GetTopLevelWindows();

    /// <summary>Details of any window (top-level or child); null when the handle doesn't identify an existing window.</summary>
    WindowDetails? GetWindowDetails(WindowHandle hwnd);

    /// <summary>
    /// All visible descendant windows, in enumeration (z/tab) order, using only messages that give up on a hung
    /// target. Used when UI Automation can't answer for a window.
    /// </summary>
    /// <param name="includeHidden">Also hidden windows (for dialog-template matching: a hidden control still exists).</param>
    IReadOnlyList<ChildWindow> GetChildWindows(WindowHandle window, bool includeHidden = false);

    /// <summary>
    /// Whether the window's thread is processing messages right now (it answers a WM_NULL within a short timeout).
    /// Immediate, unlike Windows' own hung flag, which takes ~5 s to appear.
    /// </summary>
    bool AnswersMessages(WindowHandle window);

    /// <summary>Whether WinMCP itself runs elevated (then UIPI doesn't separate it from elevated targets).</summary>
    bool CurrentProcessElevated { get; }
}
