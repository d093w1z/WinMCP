using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinMcp.Windows;

/// <summary>Making a window the foreground window, as far as Windows allows it (keyboard input, native menus).</summary>
internal static unsafe class Foreground
{
    private static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(750);

    /// <summary>
    /// Windows lets a process change the foreground only if, among other conditions, it received the last input event.
    /// A zero-distance mouse move makes that true without side effects (an Alt tap, the usual trick, would activate
    /// the menu bar of whatever application currently has focus).
    /// </summary>
    public static bool Bring(HWND hwnd)
    {
        if (Is(hwnd))
            return true;
        var nudge = new INPUT { type = INPUT_TYPE.INPUT_MOUSE };
        nudge.Anonymous.mi.dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE; // dx = dy = 0
        PInvoke.SendInput([nudge], sizeof(INPUT));

        PInvoke.BringWindowToTop(hwnd);
        PInvoke.SetForegroundWindow(hwnd);
        var wait = Stopwatch.StartNew();
        while (wait.Elapsed < Wait)
        {
            if (Is(hwnd))
                return true;
            Thread.Sleep(25);
        }
        return false;
    }

    /// <summary>
    /// What to tell the user when <see cref="Bring"/> fails. No foreground window at all usually means a locked
    /// workstation or a disconnected session (seen in M11), where no window can be activated.
    /// </summary>
    public static string RefusedHint() => PInvoke.GetForegroundWindow().IsNull
        ? "No window is active at all: the desktop is probably locked or the remote session disconnected. Ask the user to unlock it, then retry."
        : "Windows only lets the foreground change in some situations. Ask the user to click the application once, then retry.";

    /// <summary>Only the window itself (or a child of it) counts; an owned dialog is a different top-level window.</summary>
    public static bool Is(HWND hwnd)
    {
        var foreground = PInvoke.GetForegroundWindow();
        return !foreground.IsNull && (foreground == hwnd || PInvoke.GetAncestor(foreground, GET_ANCESTOR_FLAGS.GA_ROOT) == hwnd);
    }
}
