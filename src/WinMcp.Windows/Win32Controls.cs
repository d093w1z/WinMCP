using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinMcp.Windows;

/// <summary>Reads Win32 control facts by message, never blocking on a hung target.</summary>
internal static unsafe class Win32Controls
{
    private const uint MessageTimeoutMs = 1000;
    private const int MaxOptions = WinMcp.Core.Automation.ElementExtras.MaxOptions;
    private const uint CbsOwnerDrawFixed = 0x0010, CbsOwnerDrawVariable = 0x0020, CbsHasStrings = 0x0200;

    /// <returns>Null for top-level windows, where the value is a menu handle rather than a control ID.</returns>
    public static int? ControlId(nint hwnd, nint topLevel) =>
        hwnd == 0 || hwnd == topLevel ? null : PInvoke.GetDlgCtrlID((HWND)hwnd);

    /// <summary>
    /// Items of a combo box without opening it (M0: collapsed combos expose no items to UIA, and expanding would
    /// fire the app's DropDown handlers). CB_* messages are below WM_USER, so Windows marshals their string
    /// buffers across processes.
    /// </summary>
    /// <returns>
    /// The first <see cref="MaxOptions"/> items and the total item count; null when the window isn't a combo box
    /// or its items aren't stored as strings (owner-drawn).
    /// </returns>
    public static (IReadOnlyList<string> Options, int Count)? ComboOptions(nint hwnd, string className)
    {
        if (hwnd == 0 || !className.Contains("COMBOBOX", StringComparison.OrdinalIgnoreCase))
            return null;

        var style = (uint)PInvoke.GetWindowLong((HWND)hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        if ((style & (CbsOwnerDrawFixed | CbsOwnerDrawVariable)) != 0 && (style & CbsHasStrings) == 0)
            return null;

        if (Send(hwnd, PInvoke.CB_GETCOUNT, 0, 0) is not { } count || count < 0)
            return null;

        var options = new List<string>((int)Math.Min(count, MaxOptions));
        for (var i = 0; i < Math.Min(count, MaxOptions); i++)
        {
            if (Send(hwnd, PInvoke.CB_GETLBTEXTLEN, (nuint)i, 0) is not { } length || length < 0)
                return null;
            var buffer = new char[length + 1];
            fixed (char* p = buffer)
            {
                if (Send(hwnd, PInvoke.CB_GETLBTEXT, (nuint)i, (nint)p) is not { } copied || copied < 0)
                    return null;
                options.Add(new string(p, 0, (int)Math.Min(copied, length)));
            }
        }
        return (options, (int)count);
    }

    /// <summary>
    /// Selects a combo item the way a user would be seen by the app: CB_SETCURSEL alone does <b>not</b> send
    /// CBN_SELCHANGE, so the app's selection handler would never run. The notification is sent explicitly.
    /// </summary>
    public static bool SelectComboItem(nint hwnd, int index)
    {
        if (Send(hwnd, PInvoke.CB_SETCURSEL, (nuint)index, 0) is not { } result || result != index)
            return false;
        var parent = PInvoke.GetParent((HWND)hwnd);
        var id = (uint)PInvoke.GetDlgCtrlID((HWND)hwnd) & 0xFFFF;
        var wParam = (nuint)((PInvoke.CBN_SELCHANGE << 16) | id);
        return Send(parent, PInvoke.WM_COMMAND, wParam, hwnd) is not null;
    }

    /// <summary>Posted, not sent: a click handler may run for a long time or open a modal dialog.</summary>
    public static bool PostClick(nint hwnd) => PInvoke.PostMessage((HWND)hwnd, PInvoke.BM_CLICK, default, default);

    public static bool SetText(nint hwnd, string text)
    {
        fixed (char* p = text)
            return Send(hwnd, PInvoke.WM_SETTEXT, 0, (nint)p) is > 0;
    }

    private static long? Send(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        nuint result;
        var ok = PInvoke.SendMessageTimeout(
            (HWND)hwnd, message, new WPARAM(wParam), new LPARAM(lParam),
            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG | SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_BLOCK,
            MessageTimeoutMs, &result);
        return ok == 0 ? null : (long)(nint)result; // CB_ERR (-1) comes back as a negative value
    }
}
