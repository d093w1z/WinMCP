using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using WinMcp.Core.Automation;
using WinMcp.Core.Errors;

namespace WinMcp.Windows.Automation;

/// <summary>Action implementations: UIA patterns first, Win32 message fallbacks only where they reach the app's handlers.</summary>
internal static class UiaActions
{
    public static ActionOutcome Perform(UIA3Automation automation, AutomationElement element, ElementAction action) => action switch
    {
        ElementAction.Invoke => Invoke(element),
        ElementAction.SetValue set => SetValue(element, set.Value),
        ElementAction.Select select => Select(automation, element, select.Option),
        ElementAction.SetToggle toggle => SetToggle(element, toggle.On),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    private static ActionOutcome Invoke(AutomationElement element)
    {
        var patterns = element.Patterns;
        if (patterns.Invoke.PatternOrDefault is { } invoke)
        {
            try
            {
                invoke.Invoke();
            }
            catch (Exception ex) when (UiaErrors.IsTimeout(ex))
            {
                // Some providers run the click handler synchronously; a long handler or a modal dialog then outlasts
                // the UIA timeout. The click was delivered, so report success rather than inviting a double click.
                return new ActionOutcome("uia.InvokePattern", true,
                    Warning: "The application is still busy handling the click (it may have opened a modal dialog). Check with list_windows/inspect_window before acting again.");
            }
            return new ActionOutcome("uia.InvokePattern", true);
        }
        if (patterns.Toggle.PatternOrDefault is { } toggle)
        {
            toggle.Toggle();
            return new ActionOutcome("uia.TogglePattern", true, StateAfter: Wire(toggle.ToggleState.Value));
        }
        if (patterns.SelectionItem.PatternOrDefault is { } item)
        {
            item.Select();
            return new ActionOutcome("uia.SelectionItemPattern", true);
        }
        if (patterns.ExpandCollapse.PatternOrDefault is { } expand)
        {
            if (expand.ExpandCollapseState.Value == ExpandCollapseState.Collapsed) expand.Expand(); else expand.Collapse();
            return new ActionOutcome("uia.ExpandCollapsePattern", true);
        }
        var hwnd = element.Properties.NativeWindowHandle.ValueOrDefault;
        if (hwnd != 0 && ClassName(element).Contains("BUTTON", StringComparison.OrdinalIgnoreCase) && Win32Controls.PostClick(hwnd))
            return new ActionOutcome("win32.BM_CLICK", true);

        throw NotSupported(element, "invoked (no Invoke, Toggle, SelectionItem or ExpandCollapse pattern, and not a Win32 button)");
    }

    private static ActionOutcome SetValue(AutomationElement element, string text)
    {
        if (element.Patterns.Value.PatternOrDefault is { } value)
        {
            if (value.IsReadOnly.ValueOrDefault)
                throw NotSupported(element, "edited: its value is read-only");
            var before = value.Value.ValueOrDefault;
            value.SetValue(text);
            var after = value.Value.ValueOrDefault;
            return new ActionOutcome("uia.ValuePattern", !string.Equals(before, after, StringComparison.Ordinal), ValueAfter: after);
        }
        var hwnd = element.Properties.NativeWindowHandle.ValueOrDefault;
        if (hwnd != 0 && ClassName(element).Contains("EDIT", StringComparison.OrdinalIgnoreCase) && Win32Controls.SetText(hwnd, text))
            return new ActionOutcome("win32.WM_SETTEXT", true, ValueAfter: text);

        throw NotSupported(element, "edited (no Value pattern and not a Win32 edit control)");
    }

    /// <summary>
    /// Expands if needed (collapsed combos expose no items, M0), selects the named item, and restores the collapsed
    /// state (UIA leaves it open, M0). Native combos whose items UIA can't see fall back to CB_SETCURSEL + CBN_SELCHANGE.
    /// </summary>
    private static ActionOutcome Select(UIA3Automation automation, AutomationElement container, string option)
    {
        var expand = container.Patterns.ExpandCollapse.PatternOrDefault;
        var wasCollapsed = expand?.ExpandCollapseState.ValueOrDefault == ExpandCollapseState.Collapsed;
        try
        {
            if (wasCollapsed)
                expand!.Expand();

            var selectable = new PropertyCondition(
                automation.PatternLibrary.SelectionItemPattern.AvailabilityProperty
                ?? throw new InvalidOperationException("UIA3 exposes no SelectionItem availability property."),
                true);
            var items = container.FindAllDescendants(selectable);
            var match = items.FirstOrDefault(i => string.Equals(i.Properties.Name.ValueOrDefault, option, StringComparison.Ordinal))
                        ?? items.FirstOrDefault(i => string.Equals(i.Properties.Name.ValueOrDefault, option, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                var item = match.Patterns.SelectionItem.Pattern;
                if (item.IsSelected.ValueOrDefault)
                    return new ActionOutcome("none", false, ValueAfter: CurrentValue(container) ?? match.Properties.Name.ValueOrDefault);
                item.Select();
                return new ActionOutcome("uia.SelectionItemPattern", true, ValueAfter: CurrentValue(container) ?? match.Properties.Name.ValueOrDefault);
            }

            var hwnd = container.Properties.NativeWindowHandle.ValueOrDefault;
            if (items.Length == 0 && Win32Controls.ComboOptions(hwnd, ClassName(container)) is { } combo)
            {
                var index = IndexOf(combo.Options, option);
                if (index < 0)
                    throw OptionNotFound(container, option, combo.Options);
                if (CurrentValue(container) == combo.Options[index])
                    return new ActionOutcome("none", false, ValueAfter: combo.Options[index]);
                if (!Win32Controls.SelectComboItem(hwnd, index))
                    throw NotSupported(container, "changed through Win32 messages");
                return new ActionOutcome("win32.CB_SETCURSEL+CBN_SELCHANGE", true, ValueAfter: CurrentValue(container) ?? combo.Options[index]);
            }

            if (items.Length == 0 && container.Patterns.Selection.PatternOrDefault is null && expand is null)
                throw NotSupported(container, "used to select options (it has no selectable items)");
            throw OptionNotFound(container, option, items.Select(i => i.Properties.Name.ValueOrDefault ?? "").ToList());
        }
        finally
        {
            if (wasCollapsed && expand!.ExpandCollapseState.ValueOrDefault != ExpandCollapseState.Collapsed)
                expand.Collapse();
        }
    }

    /// <summary>Sets a target state rather than flipping, so retries are harmless. Tri-state boxes may need two toggles.</summary>
    private static ActionOutcome SetToggle(AutomationElement element, bool on)
    {
        var toggle = element.Patterns.Toggle.PatternOrDefault ?? throw NotSupported(element, "toggled (no Toggle pattern)");
        var target = on ? ToggleState.On : ToggleState.Off;
        var state = toggle.ToggleState.Value;
        if (state == target)
            return new ActionOutcome("none", false, StateAfter: Wire(state));
        for (var i = 0; i < 3 && state != target; i++)
        {
            toggle.Toggle();
            state = toggle.ToggleState.Value;
        }
        if (state != target)
            throw NotSupported(element, $"set to '{Wire(target)}' (it stays '{Wire(state)}')");
        return new ActionOutcome("uia.TogglePattern", true, StateAfter: Wire(state));
    }

    private static string? CurrentValue(AutomationElement element) => element.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault;

    private static int IndexOf(IReadOnlyList<string> options, string option)
    {
        for (var i = 0; i < options.Count; i++)
            if (string.Equals(options[i], option, StringComparison.Ordinal)) return i;
        for (var i = 0; i < options.Count; i++)
            if (string.Equals(options[i], option, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static string ClassName(AutomationElement element) => element.Properties.ClassName.ValueOrDefault ?? "";

    private static string Wire(ToggleState state) => state switch
    {
        ToggleState.On => "on",
        ToggleState.Off => "off",
        _ => "indeterminate",
    };

    private static WinMcpException NotSupported(AutomationElement element, string what) =>
        new(new WinMcpError(
            WinMcpErrorCode.PatternNotSupported,
            $"{element.Properties.ControlType.ValueOrDefault} \"{element.Properties.Name.ValueOrDefault}\" can't be {what}.",
            Hint: "inspect_element lists the patterns this element supports."));

    private static WinMcpException OptionNotFound(AutomationElement container, string option, IReadOnlyList<string> available) =>
        new(new WinMcpError(
            WinMcpErrorCode.OptionNotFound,
            $"\"{container.Properties.Name.ValueOrDefault}\" has no option '{option}'.",
            Hint: "Use one of the available options.",
            Details: new Dictionary<string, object?> { ["available"] = available.Take(ElementExtras.MaxOptions).ToList() }));
}
