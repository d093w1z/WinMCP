using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;

namespace WinMcp.Windows;

/// <summary>
/// Actions and details for elements of the Win32 fallback tree (built when UI Automation can't answer). Only Win32
/// messages that reach the application's own handlers are used; nothing here touches UI Automation.
/// </summary>
internal static class Win32Actions
{
    public static ActionOutcome Perform(WindowHandle handle, ElementAction action)
    {
        var hwnd = (HWND)(nint)handle.Value;
        if (!PInvoke.IsWindow(hwnd))
            throw Stale();
        if (!PInvoke.IsWindowEnabled(hwnd))
            throw new WinMcpException(new WinMcpError(WinMcpErrorCode.ElementDisabled, "The element became disabled."));

        var className = ClassName(hwnd);
        var style = (uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        var isButton = Win32Controls.ClassIs(className, "BUTTON");
        var check = Win32Controls.ReadCheck((nint)handle.Value, className, style);

        switch (action)
        {
            case ElementAction.Invoke when isButton:
                return Automation.UiaActions.ClickOutcome(Win32Controls.PostClick((nint)handle.Value))
                    ?? throw NotSupported(className, "clicked");

            case ElementAction.SetToggle toggle when check is not null:
                var target = toggle.On ? 1 : 0;
                if (check == target)
                    return new ActionOutcome("none", false, StateAfter: Wire(check));
                // A click cycles the state (tri-state boxes need two); the handler runs from the message loop.
                for (var i = 0; i < 3 && Win32Controls.ReadCheck((nint)handle.Value, className, style) != target; i++)
                {
                    Win32Controls.PostClick((nint)handle.Value);
                    WaitFor(() => Win32Controls.ReadCheck((nint)handle.Value, className, style) != check);
                    check = Win32Controls.ReadCheck((nint)handle.Value, className, style);
                }
                return new ActionOutcome("win32.BM_CLICK", true, StateAfter: Wire(Win32Controls.ReadCheck((nint)handle.Value, className, style)));

            case ElementAction.SetValue set when Win32Controls.ClassIs(className, "EDIT") || className.StartsWith("RICHEDIT", StringComparison.OrdinalIgnoreCase):
                if (Win32Controls.IsPasswordEdit(hwnd, className))
                    throw new WinMcpException(new WinMcpError(WinMcpErrorCode.PasswordField, "This is a password field; WinMCP does not enter passwords."));
                if (!Win32Controls.SetText((nint)handle.Value, set.Value))
                    throw NotSupported(className, "edited");
                var after = Win32Controls.ReadText((nint)handle.Value);
                return new ActionOutcome("win32.WM_SETTEXT", true, ValueAfter: after);

            case ElementAction.Select select when Win32Controls.ClassIs(className, "COMBOBOX"):
                var combo = Win32Controls.ComboOptions((nint)handle.Value, className) ?? throw NotSupported(className, "read (owner-drawn items)");
                var index = combo.Options.ToList().FindIndex(o => string.Equals(o, select.Option, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                    throw new WinMcpException(new WinMcpError(WinMcpErrorCode.OptionNotFound, $"No option '{select.Option}'.",
                        Hint: "Use one of the available options.", Details: new Dictionary<string, object?> { ["available"] = combo.Options }));
                if (!Win32Controls.SelectComboItem((nint)handle.Value, index))
                    throw NotSupported(className, "changed");
                return new ActionOutcome("win32.CB_SETCURSEL+CBN_SELCHANGE", true, ValueAfter: combo.Options[index]);

            default:
                throw new WinMcpException(new WinMcpError(WinMcpErrorCode.PatternNotSupported,
                    $"'{action.Name}' isn't available for a {className} control while UI Automation is unavailable.",
                    Hint: "Win32 fallback supports: invoke (buttons), set_toggle (check boxes), set_value (edits), select_option (combo boxes). "
                          + "Close any open dialog so UI Automation can answer again."));
        }
    }

    public static ElementExtras Extras(WindowHandle handle)
    {
        var hwnd = (HWND)(nint)handle.Value;
        if (!PInvoke.IsWindow(hwnd))
            throw Stale();
        var className = ClassName(hwnd);
        var style = (uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        var capabilities = new List<string>();
        if (Win32Controls.ClassIs(className, "BUTTON"))
            capabilities.Add(Win32Controls.ReadCheck((nint)handle.Value, className, style) is null ? "Invoke" : "Toggle");
        if (Win32Controls.ClassIs(className, "EDIT"))
            capabilities.Add("Value");
        var options = Win32Controls.ComboOptions((nint)handle.Value, className);
        if (options is not null)
            capabilities.Add("Selection");
        return new ElementExtras(
            FrameworkId: "Win32 (fallback)",
            IsKeyboardFocusable: PInvoke.IsWindowEnabled(hwnd),
            HelpText: "",
            LabeledByRuntimeId: null,
            Patterns: capabilities,
            ControlId: PInvoke.GetDlgCtrlID(hwnd),
            Options: options?.Options,
            OptionCount: options?.Count);
    }

    private static unsafe string ClassName(HWND hwnd)
    {
        var buffer = stackalloc char[257];
        return new string(buffer, 0, PInvoke.GetClassName(hwnd, buffer, 257));
    }

    private static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(500);
        while (!condition() && DateTime.UtcNow < deadline)
            Thread.Sleep(20);
    }

    private static string Wire(int? check) => check switch { 1 => "on", 2 => "indeterminate", _ => "off" };

    private static WinMcpException Stale() =>
        new(new WinMcpError(WinMcpErrorCode.ElementStale, "The control no longer exists.", Hint: "Call get_ui_tree again."));

    private static WinMcpException NotSupported(string className, string what) =>
        new(new WinMcpError(WinMcpErrorCode.PatternNotSupported, $"This {className} control can't be {what} through Win32 messages."));
}
