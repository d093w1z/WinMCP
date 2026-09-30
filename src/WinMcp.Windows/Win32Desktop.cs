using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Security;
using Windows.Win32.System.SystemInformation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;
using WinMcp.Core.Desktop;

namespace WinMcp.Windows;

/// <summary>
/// <see cref="IDesktop"/> over plain Win32. Uses only calls that don't send messages to the target window,
/// so a hung application can't block enumeration (GetWindowText reads the cached caption for other processes).
/// </summary>
public sealed unsafe class Win32Desktop : IDesktop
{
    public IReadOnlyList<WindowInfo> GetTopLevelWindows()
    {
        using var dpi = DpiScope.Enter();
        var handles = new List<HWND>();
        PInvoke.EnumWindows((hwnd, _) =>
        {
            handles.Add(hwnd);
            return true;
        }, default);

        var foreground = PInvoke.GetForegroundWindow();
        var processes = new Dictionary<uint, ProcessInfo>();
        var windows = new List<WindowInfo>(handles.Count);
        foreach (var hwnd in handles)
        {
            if (ReadWindow(hwnd, foreground, processes, out _) is { } window)
                windows.Add(window);
        }
        return windows;
    }

    public WindowDetails? GetWindowDetails(WindowHandle handle)
    {
        using var dpi = DpiScope.Enter();
        var hwnd = (HWND)(nint)handle.Value;
        if (!PInvoke.IsWindow(hwnd) || ReadWindow(hwnd, PInvoke.GetForegroundWindow(), [], out var threadId) is not { } window)
            return null;

        var parent = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_PARENT);
        var childClasses = new List<string>();
        PInvoke.EnumChildWindows(hwnd, (child, _) =>
        {
            childClasses.Add(ReadClassName(child));
            return true;
        }, default);

        return new WindowDetails(
            window,
            Parent: parent.IsNull || parent == PInvoke.GetDesktopWindow() ? null : ToHandle(parent),
            Style: (uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE),
            ExStyle: (uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE),
            Responding: !PInvoke.IsHungAppWindow(hwnd),
            ThreadId: (int)threadId,
            childClasses);
    }

    public bool AnswersMessages(WindowHandle window) => Win32Controls.Ping((nint)window.Value);

    public IReadOnlyList<ChildWindow> GetChildWindows(WindowHandle window, bool includeHidden = false)
    {
        using var dpi = DpiScope.Enter();
        var top = (HWND)(nint)window.Value;
        var handles = new List<HWND>();
        PInvoke.EnumChildWindows(top, (child, _) =>
        {
            handles.Add(child);
            return true;
        }, default);

        var children = new List<ChildWindow>(handles.Count);
        foreach (var hwnd in handles)
        {
            var visible = PInvoke.IsWindowVisible(hwnd);
            if (!visible && !includeHidden)
                continue;
            var className = ReadClassName(hwnd);
            var style = (uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
            PInvoke.GetWindowRect(hwnd, out var rect);
            var parent = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_PARENT);
            children.Add(new ChildWindow(
                ToHandle(hwnd),
                ToHandle(parent),
                className,
                Win32Controls.IsPasswordEdit(hwnd, className) ? "" : Win32Controls.ReadText((nint)hwnd.Value),
                PInvoke.GetDlgCtrlID(hwnd),
                style,
                visible,
                PInvoke.IsWindowEnabled(hwnd),
                new Rect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top),
                Win32Controls.ReadCheck((nint)hwnd.Value, className, style)));
        }
        return children;
    }

    /// <returns>Null when the window was destroyed while being read.</returns>
    private static WindowInfo? ReadWindow(HWND hwnd, HWND foreground, Dictionary<uint, ProcessInfo> processes, out uint threadId)
    {
        uint pid;
        threadId = PInvoke.GetWindowThreadProcessId(hwnd, &pid);
        if (threadId == 0)
            return null;

        if (!processes.TryGetValue(pid, out var process))
            processes[pid] = process = ReadProcess(pid);

        var owner = PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_OWNER);
        return new WindowInfo(
            Hwnd: ToHandle(hwnd),
            Title: ReadTitle(hwnd),
            ClassName: ReadClassName(hwnd),
            Process: process,
            Visible: PInvoke.IsWindowVisible(hwnd),
            Cloaked: IsCloaked(hwnd),
            Enabled: PInvoke.IsWindowEnabled(hwnd),
            Minimized: PInvoke.IsIconic(hwnd),
            Maximized: PInvoke.IsZoomed(hwnd),
            Foreground: hwnd == foreground,
            Bounds: ReadBounds(hwnd),
            Dpi: (int)PInvoke.GetDpiForWindow(hwnd),
            Owner: owner.IsNull ? null : ToHandle(owner));
    }

    private static WindowHandle ToHandle(HWND hwnd) => new((nint)hwnd.Value);

    private static string ReadTitle(HWND hwnd)
    {
        var length = PInvoke.GetWindowTextLength(hwnd);
        if (length <= 0) return "";
        var buffer = new char[length + 1];
        fixed (char* p = buffer)
        {
            var copied = PInvoke.GetWindowText(hwnd, p, buffer.Length);
            return new string(p, 0, copied);
        }
    }

    private static string ReadClassName(HWND hwnd)
    {
        const int MaxClassName = 256; // documented maximum class name length
        var buffer = stackalloc char[MaxClassName + 1];
        var copied = PInvoke.GetClassName(hwnd, buffer, MaxClassName + 1);
        return new string(buffer, 0, copied);
    }

    private static bool IsCloaked(HWND hwnd)
    {
        uint cloaked = 0;
        return PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(uint)).Succeeded
            && cloaked != 0;
    }

    private static Rect ReadBounds(HWND hwnd)
    {
        RECT rect;
        if (PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &rect, (uint)sizeof(RECT)).Failed
            && !PInvoke.GetWindowRect(hwnd, out rect))
            return default;
        return new Rect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
    }

    private static ProcessInfo ReadProcess(uint pid)
    {
        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process.IsInvalid)
            return new ProcessInfo((int)pid, NameFromSystem(pid), Path: null, Architecture: null, Elevated: null);

        var path = ReadImagePath(process);
        var name = path is not null ? System.IO.Path.GetFileNameWithoutExtension(path) : NameFromSystem(pid);
        return new ProcessInfo((int)pid, name, path, ReadArchitecture(process), ReadElevation(process));
    }

    private static string? ReadImagePath(SafeHandle process)
    {
        Span<char> buffer = new char[32 * 1024]; // long-path limit
        var size = (uint)buffer.Length;
        return PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, ref size)
            ? new string(buffer[..(int)size])
            : null;
    }

    private static string? ReadArchitecture(SafeHandle process)
    {
        if (!PInvoke.IsWow64Process2(process, out var processMachine, out var nativeMachine))
            return null;
        // IMAGE_FILE_MACHINE_UNKNOWN means "not running under WOW64", i.e. the native architecture.
        var machine = processMachine == IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_UNKNOWN ? nativeMachine : processMachine;
        return machine switch
        {
            IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_AMD64 => "x64",
            IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_I386 => "x86",
            IMAGE_FILE_MACHINE.IMAGE_FILE_MACHINE_ARM64 => "arm64",
            _ => null,
        };
    }

    private static bool? ReadElevation(SafeHandle process)
    {
        if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, out var token))
            return null;
        using (token)
        {
            TOKEN_ELEVATION elevation;
            uint returned;
            // No SafeHandle overload is generated for this API; `using (token)` keeps the handle alive.
            return PInvoke.GetTokenInformation((HANDLE)token.DangerousGetHandle(), TOKEN_INFORMATION_CLASS.TokenElevation, &elevation, (uint)sizeof(TOKEN_ELEVATION), &returned)
                ? elevation.TokenIsElevated != 0
                : null;
        }
    }

    private static string NameFromSystem(uint pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return ""; // process exited
        }
    }
}
