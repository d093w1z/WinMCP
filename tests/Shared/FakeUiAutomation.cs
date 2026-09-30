using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;

namespace WinMcp.Testing;

/// <summary>In-memory <see cref="IUiAutomation"/> serving canned trees per window.</summary>
internal sealed class FakeUiAutomation : IUiAutomation
{
    public Dictionary<WindowHandle, RawElement> Trees { get; } = [];

    /// <summary>Extras per runtime id; elements without an entry get <see cref="DefaultExtras"/>.</summary>
    public Dictionary<string, ElementExtras> Extras { get; } = [];

    public static readonly ElementExtras DefaultExtras = new("WinForm", true, "", null, ["Invoke"], null, null);

    public Exception? ThrowOnFetch { get; set; }

    public int FetchCount { get; private set; }

    /// <summary>Explicit extras, or defaults carrying the element's bounds from its canned tree (as the real one reads them live).</summary>
    public Task<ElementExtras> GetElementExtrasAsync(ElementKey element, CancellationToken cancellationToken)
    {
        if (Extras.TryGetValue(element.RuntimeId, out var extras))
            return Task.FromResult(extras);
        var bounds = Trees.TryGetValue(element.Window, out var tree)
            ? tree.DescendantsAndSelf().FirstOrDefault(e => e.RuntimeId == element.RuntimeId)?.Bounds ?? default
            : default;
        return Task.FromResult(DefaultExtras with { Bounds = bounds });
    }

    /// <summary>Every action that reached the "target", in order.</summary>
    public List<(ElementKey Element, ElementAction Action)> Performed { get; } = [];

    /// <summary>Simulates the action's effect (e.g. mutate <see cref="Trees"/>); default reports a generic success.</summary>
    public Func<ElementKey, ElementAction, ActionOutcome>? OnPerform { get; set; }

    public Task<ActionOutcome> PerformAsync(ElementKey element, ElementAction action, CancellationToken cancellationToken)
    {
        Performed.Add((element, action));
        try
        {
            return Task.FromResult(OnPerform?.Invoke(element, action) ?? new ActionOutcome("fake.Action", true));
        }
        catch (Exception ex)
        {
            return Task.FromException<ActionOutcome>(ex);
        }
    }

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
