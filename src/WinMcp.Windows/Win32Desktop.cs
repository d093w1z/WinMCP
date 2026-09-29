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
            uint pid;
            if (PInvoke.GetWindowThreadProcessId(hwnd, &pid) == 0)
                continue; // window destroyed during enumeration

            if (!processes.TryGetValue(pid, out var process))
                processes[pid] = process = ReadProcess(pid);

            var owner = PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_OWNER);
            windows.Add(new WindowInfo(
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
                Owner: owner.IsNull ? null : ToHandle(owner)));
        }
        return windows;
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
