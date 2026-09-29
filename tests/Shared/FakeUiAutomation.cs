using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;

namespace WinMcp.Testing;

/// <summary>In-memory <see cref="IUiAutomation"/> serving canned trees per window.</summary>
internal sealed class FakeUiAutomation : IUiAutomation
{
    public Dictionary<WindowHandle, RawElement> Trees { get; } = [];

    public Exception? ThrowOnFetch { get; set; }

    public int FetchCount { get; private set; }

    public Task<RawElement> GetWindowTreeAsync(WindowHandle window, CancellationToken cancellationToken)
    {
        FetchCount++;
        if (ThrowOnFetch is { } ex)
            return Task.FromException<RawElement>(ex);
        return Trees.TryGetValue(window, out var tree)
            ? Task.FromResult(tree)
            : Task.FromException<RawElement>(new WinMcpException(new WinMcpError(WinMcpErrorCode.WindowClosed, "gone")));
    }

    public static RawElement Element(
        string runtimeId,
        string controlType,
        string name = "",
        string automationId = "",
        string? value = null,
        bool enabled = true,
        bool offscreen = false,
        bool password = false,
        string? toggle = null,
        string? expand = null,
        bool? selected = null,
        string className = "") =>
        new(runtimeId, controlType, name, automationId, className, 0, new Rect(0, 0, 10, 10),
            enabled, offscreen, password, HasKeyboardFocus: false, value, toggle, expand, selected, []);

    /// <summary>Raw UIA shape of TestApp v1 as observed in M0, including the noise the normalizer removes.</summary>
    public static RawElement TestAppTree() =>
        Element("1", "Window", "WinMCP Test App", "MainForm").With(
            Element("1.1", "Text", "Name:", "nameLabel"),
            Element("1.2", "Edit", "Name:", "nameTextBox", value: ""),
            Element("1.3", "Text", "Type:", "typeLabel"),
            Element("1.4", "ComboBox", "Type:", "typeComboBox", value: "Text", expand: "collapsed").With(
                Element("1.4.1", "Text", "Type:"),
                Element("1.4.2", "Button", "Open")),
            Element("1.5", "CheckBox", "Enable feature", "enableCheckBox", toggle: "on"),
            Element("1.6", "Button", "Apply", "applyButton"),
            Element("1.7", "Button", "Cancel", "cancelButton"),
            Element("1.8", "Button", "Advanced...", "advancedButton", enabled: false),
            Element("1.9", "Text", "Status: Ready", "statusLabel"),
            Element("1.10", "TitleBar", "", "TitleBar").With(
                Element("1.10.1", "MenuBar", "System", "SystemMenuBar"),
                Element("1.10.2", "Button", "Close", "Close")));
}

internal static class RawElementBuilder
{
    public static RawElement With(this RawElement element, params RawElement[] children) => element with { Children = children };
}
