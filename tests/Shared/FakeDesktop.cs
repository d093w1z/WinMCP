using WinMcp.Core.Desktop;

namespace WinMcp.Testing;

/// <summary>In-memory <see cref="IDesktop"/> for tests that must not depend on the real desktop.</summary>
internal sealed class FakeDesktop(params WindowInfo[] windows) : IDesktop
{
    public List<WindowInfo> Windows { get; } = [.. windows];

    public Exception? ThrowOnEnumerate { get; set; }

    public IReadOnlyList<WindowInfo> GetTopLevelWindows() => ThrowOnEnumerate is { } ex ? throw ex : Windows;

    public static WindowInfo Window(
        string process,
        string title,
        int pid = 100,
        long hwnd = 0x1000,
        bool visible = true,
        bool cloaked = false,
        string? path = null) =>
        new(
            Hwnd: new WindowHandle(hwnd),
            Title: title,
            ClassName: "TestWindowClass",
            Process: new ProcessInfo(pid, process, path ?? $@"C:\Apps\{process}.exe", "x64", Elevated: false),
            Visible: visible,
            Cloaked: cloaked,
            Enabled: true,
            Minimized: false,
            Maximized: false,
            Foreground: false,
            Bounds: new Rect(10, 20, 300, 200),
            Dpi: 96,
            Owner: null);
}
