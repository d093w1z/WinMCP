namespace WinMcp.Core.Desktop;

/// <summary>Screen rectangle in physical pixels, virtual-screen coordinates (may be negative on multi-monitor setups).</summary>
public readonly record struct Rect(int X, int Y, int Width, int Height);

/// <param name="Name">Executable name without extension, e.g. <c>WinMcp.TestApp</c>.</param>
/// <param name="Path">Full image path; null when the process can't be queried.</param>
/// <param name="Architecture"><c>x86</c>, <c>x64</c> or <c>arm64</c>; null when unknown.</param>
/// <param name="Elevated">Null when the token can't be read (typically: elevated or protected process).</param>
public sealed record ProcessInfo(int Pid, string Name, string? Path, string? Architecture, bool? Elevated);

/// <param name="Bounds">Visible frame (DWM extended frame bounds), not the invisible resize border — see M0 finding 12.</param>
/// <param name="Cloaked">Hidden by DWM (e.g. suspended UWP apps, windows on other virtual desktops) despite being "visible".</param>
public sealed record WindowInfo(
    WindowHandle Hwnd,
    string Title,
    string ClassName,
    ProcessInfo Process,
    bool Visible,
    bool Cloaked,
    bool Enabled,
    bool Minimized,
    bool Maximized,
    bool Foreground,
    Rect Bounds,
    int Dpi,
    WindowHandle? Owner)
{
    /// <summary>What a user would consider "on screen": visible and not cloaked.</summary>
    public bool Shown => Visible && !Cloaked;
}
