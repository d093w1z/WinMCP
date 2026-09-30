using WinMcp.Core.Desktop;

namespace WinMcp.Testing;

/// <summary>In-memory <see cref="IDesktop"/> for tests that must not depend on the real desktop.</summary>
internal sealed class FakeDesktop(params WindowInfo[] windows) : IDesktop
{
    public List<WindowInfo> Windows { get; } = [.. windows];

    /// <summary>Explicit details per handle; windows without an entry get <see cref="DefaultDetails"/>.</summary>
    public Dictionary<WindowHandle, WindowDetails> Details { get; } = [];

    public Exception? ThrowOnEnumerate { get; set; }

    /// <summary>Child windows per top-level window, for the Win32 fallback tree.</summary>
    public Dictionary<WindowHandle, List<ChildWindow>> Children { get; } = [];

    public IReadOnlyList<ChildWindow> GetChildWindows(WindowHandle window, bool includeHidden = false) =>
        Children.TryGetValue(window, out var children) ? children.Where(c => includeHidden || c.Visible).ToList() : [];

    /// <summary>Mirrors <see cref="WindowDetails.Responding"/> unless overridden per window.</summary>
    public HashSet<WindowHandle> NotAnsweringMessages { get; } = [];

    public bool AnswersMessages(WindowHandle window) =>
        !NotAnsweringMessages.Contains(window) && (GetWindowDetails(window)?.Responding ?? false);

    public static ChildWindow Child(long hwnd, long parent, string className, string text, int controlId = 0, uint style = 0, bool enabled = true, int? check = null) =>
        new(new WindowHandle(hwnd), new WindowHandle(parent), className, text, controlId, style, Visible: true, enabled, new Rect(0, 0, 10, 10), check);

    public IReadOnlyList<WindowInfo> GetTopLevelWindows() => ThrowOnEnumerate is { } ex ? throw ex : Windows;

    public WindowDetails? GetWindowDetails(WindowHandle hwnd) =>
        Details.TryGetValue(hwnd, out var details) ? details
        : Windows.FirstOrDefault(w => w.Hwnd == hwnd) is { } window ? DefaultDetails(window)
        : null;

    /// <summary>A responsive, captioned top-level window with no children.</summary>
    public static WindowDetails DefaultDetails(WindowInfo window, params string[] childClasses) =>
        new(window, Parent: null, Style: 0x16CA0000, ExStyle: 0x00040100, Responding: true, ThreadId: 7, childClasses);

    public static WindowInfo Window(
        string process,
        string title,
        int pid = 100,
        long hwnd = 0x1000,
        bool visible = true,
        bool cloaked = false,
        string? path = null,
        string className = "TestWindowClass",
        long? owner = null) =>
        new(
            Hwnd: new WindowHandle(hwnd),
            Title: title,
            ClassName: className,
            Process: new ProcessInfo(pid, process, path ?? $@"C:\Apps\{process}.exe", "x64", Elevated: false),
            Visible: visible,
            Cloaked: cloaked,
            Enabled: true,
            Minimized: false,
            Maximized: false,
            Foreground: false,
            Bounds: new Rect(10, 20, 300, 200),
            Dpi: 96,
            Owner: owner is { } o ? new WindowHandle(o) : null);
}
