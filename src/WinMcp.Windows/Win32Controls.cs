using System.Runtime.InteropServices;
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

    private const uint EsPassword = 0x0020, BmGetCheck = 0x00F0, WmGetText = 0x000D, WmGetTextLength = 0x000E;

    /// <summary>Window text via WM_GETTEXT, which (unlike GetWindowText) works for controls of other processes.</summary>
    public static string ReadText(nint hwnd)
    {
        if (Send(hwnd, WmGetTextLength, 0, 0) is not { } length || length <= 0)
            return "";
        var buffer = new char[Math.Min(length, 32_000) + 1];
        fixed (char* p = buffer)
        {
            var copied = Send(hwnd, WmGetText, (nuint)buffer.Length, (nint)p) ?? 0;
            return new string(p, 0, (int)Math.Clamp(copied, 0, buffer.Length - 1));
        }
    }

    public static bool IsPasswordEdit(HWND hwnd, string className) =>
        ClassIs(className, "EDIT") && ((uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE) & EsPassword) != 0;

    /// <returns>BM_GETCHECK for check boxes and radio buttons; null for other windows.</returns>
    public static int? ReadCheck(nint hwnd, string className, uint style) =>
        ClassIs(className, "BUTTON") && (style & 0x0F) is 2 or 3 or 4 or 5 or 6 or 9 && Send(hwnd, BmGetCheck, 0, 0) is { } check
            ? (int)check
            : null;

    /// <summary>Matches a Win32 class name, including WinForms' "WindowsForms10.&lt;CLASS&gt;.app..." wrappers.</summary>
    public static bool ClassIs(string className, string win32Class) =>
        string.Equals(className, win32Class, StringComparison.OrdinalIgnoreCase)
        || (className.StartsWith("WindowsForms10.", StringComparison.OrdinalIgnoreCase)
            && className.Split('.') is [_, var inner, ..] && string.Equals(inner, win32Class, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Clicks a button with BM_CLICK, sent with a short timeout. The handler runs from the application's message
    /// processing — not inside a UI Automation call, which would block UIA for the whole application while a modal
    /// dialog is open (M9). If the handler finishes, its effect is visible when this returns; if it opens a dialog or
    /// keeps working, the send times out (the handler carries on) and the dialog already exists.
    /// (Posting BM_CLICK and syncing with a sent WM_NULL doesn't work: sent messages are processed before posted ones.)
    /// </summary>
    public static ClickResult PostClick(nint hwnd)
    {
        nuint result;
        var ok = PInvoke.SendMessageTimeout(
            (HWND)hwnd, PInvoke.BM_CLICK, default, default,
            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG | SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_BLOCK, ClickTimeoutMs, &result);
        if (ok != 0)
            return ClickResult.Handled;
        return Marshal.GetLastPInvokeError() == 1460 /* ERROR_TIMEOUT */ ? ClickResult.StillBusy : ClickResult.Failed;
    }

    private const uint ClickTimeoutMs = 750;

    /// <summary>True when the window's thread answers a WM_NULL within 500 ms, i.e. it is pumping messages.</summary>
    public static bool Ping(nint hwnd)
    {
        nuint result;
        return PInvoke.SendMessageTimeout(
            (HWND)hwnd, 0x0000 /* WM_NULL */, default, default,
            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG | SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_BLOCK, 500, &result) != 0;
    }

    public enum ClickResult
    {
        Failed,

        /// <summary>The application processed the click (or is showing the dialog it opened).</summary>
        Handled,

        /// <summary>The click was delivered but its handler was still running after the sync timeout.</summary>
        StillBusy,
    }

    public const string StillBusyWarning =
        "The click was delivered, but the application is still busy handling it. Check its state (wait_for, get_ui_tree) before acting again.";


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
