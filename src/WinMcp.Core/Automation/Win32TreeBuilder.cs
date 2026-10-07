using WinMcp.Core.Desktop;

namespace WinMcp.Core.Automation;

/// <summary>
/// Builds a <see cref="RawElement"/> tree from child windows when UI Automation can't answer (plan §A.5). Observed in
/// M9: while a WinForms click handler shows a modal dialog from inside a UIA Invoke call, UIA is blocked for the
/// whole application, but the dialog's Win32 controls still answer messages.
/// </summary>
public static class Win32TreeBuilder
{
    /// <summary>Runtime ids of fallback elements start with this, so actions know to use Win32 messages.</summary>
    public const string RuntimeIdPrefix = "win32:";

    private const uint ButtonTypeMask = 0x0F, EsPassword = 0x0020;

    public static RawElement Build(WindowInfo window, IReadOnlyList<ChildWindow> children)
    {
        var byParent = children.ToLookup(c => c.Parent);
        return new RawElement(
            RuntimeIdOf(window.Hwnd), "Window", window.Title, "", window.ClassName, window.Hwnd.Value, window.Bounds,
            window.Enabled, IsOffscreen: false, IsPassword: false, HasKeyboardFocus: false, Value: null, ToggleState: null,
            ExpandCollapseState: null, IsSelected: null, BuildChildren(window.Hwnd, byParent));
    }

    public static string RuntimeIdOf(WindowHandle hwnd) => RuntimeIdPrefix + hwnd;

    public static bool IsFallbackId(string runtimeId) => runtimeId.StartsWith(RuntimeIdPrefix, StringComparison.Ordinal);

    public static WindowHandle HandleOf(string runtimeId) =>
        WindowHandle.TryParse(runtimeId[RuntimeIdPrefix.Length..], out var handle) ? handle : throw new ArgumentException($"Not a fallback id: {runtimeId}");

    private static List<RawElement> BuildChildren(WindowHandle parent, ILookup<WindowHandle, ChildWindow> byParent)
    {
        var elements = new List<RawElement>();
        string? label = null; // the preceding static text names the next input, like UIA's own heuristic
        foreach (var child in byParent[parent].Where(c => c.Visible))
        {
            var kind = Kind(child);
            var isPassword = kind == "Edit" && (child.Style & EsPassword) != 0;
            var isInput = kind is "Edit" or "ComboBox" or "List" or "Tree";
            elements.Add(new RawElement(
                RuntimeIdOf(child.Hwnd),
                kind,
                isInput ? label ?? "" : child.Text,
                child.ControlId != 0 ? child.ControlId.ToString(System.Globalization.CultureInfo.InvariantCulture) : "",
                child.ClassName,
                child.Hwnd.Value,
                child.Bounds,
                child.Enabled,
                IsOffscreen: false,
                isPassword,
                HasKeyboardFocus: false,
                Value: kind is "Edit" or "ComboBox" && !isPassword ? child.Text : null,
                ToggleState: child.Check switch { 1 => "on", 2 => "indeterminate", 0 => "off", _ => null },
                ExpandCollapseState: null,
                IsSelected: null,
                BuildChildren(child.Hwnd, byParent)));
            label = kind == "Text" && child.Text.Length > 0 ? child.Text : isInput ? null : label;
        }
        return elements;
    }

    /// <summary>Maps a window class (WinForms wraps them as WindowsForms10.&lt;CLASS&gt;.app...) to a UIA control type.</summary>
    private static string Kind(ChildWindow child)
    {
        var name = child.ClassName.StartsWith("WindowsForms10.", StringComparison.OrdinalIgnoreCase)
            ? child.ClassName.Split('.')[1]
            : child.ClassName;
        return name.ToUpperInvariant() switch
        {
            "BUTTON" => (child.Style & ButtonTypeMask) switch
            {
                2 or 3 or 5 or 6 => "CheckBox",
                4 or 9 => "RadioButton",
                7 => "Group",
                _ => "Button",
            },
            "EDIT" or "RICHEDIT20W" or "RICHEDIT50W" => "Edit",
            "STATIC" => "Text",
            "COMBOBOX" => "ComboBox",
            "LISTBOX" or "SYSLISTVIEW32" => "List",
            "SYSTREEVIEW32" => "Tree",
            "MSCTLS_STATUSBAR32" => "StatusBar",
            "TOOLBARWINDOW32" => "ToolBar",
            "SYSTABCONTROL32" => "Tab",
            "MSCTLS_PROGRESS32" => "ProgressBar",
            "SCROLLBAR" => "ScrollBar",
            _ => "Pane",
        };
    }
}
