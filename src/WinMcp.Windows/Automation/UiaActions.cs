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
        ElementAction.Focus => Focus(element),
        ElementAction.SetExpanded expanded => SetExpanded(element, expanded.Expanded),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    /// <summary>
    /// Native (Win32) menus open only in the active window: UIA's Expand/Invoke on a menu bar item of a background
    /// window fails with "not valid due to the current state" (M11). A user's click would activate the window first,
    /// so WinMCP does the same — and reports FOCUS_FAILED if Windows refuses, instead of an internal error.
    /// </summary>
    public static void ActivateForNativeMenu(AutomationElement element, ElementAction action, WinMcp.Core.Desktop.WindowHandle window)
    {
        // Native menu items come through the MSAA proxy with no framework id ("" — "Win32" for some); WinForms and WPF
        // menus report their own and open fine in the background.
        if (action is not (ElementAction.Invoke or ElementAction.SetExpanded { Expanded: true })
            || element.Properties.ControlType.ValueOrDefault != ControlType.MenuItem
            || element.Properties.FrameworkId.ValueOrDefault is not (null or "" or "Win32"))
            return;
        var hwnd = (global::Windows.Win32.Foundation.HWND)(nint)window.Value;
        if (!Foreground.Bring(hwnd))
            throw new WinMcpException(new WinMcpError(
                WinMcpErrorCode.FocusFailed,
                $"Menus of {window} open only while it is the active window, and Windows refused to activate it.",
                Hint: Foreground.RefusedHint()));
    }

    internal static ActionOutcome? ClickOutcome(Win32Controls.ClickResult result) => result switch
    {
        Win32Controls.ClickResult.Handled => new ActionOutcome("win32.BM_CLICK", true),
        Win32Controls.ClickResult.StillBusy => new ActionOutcome("win32.BM_CLICK", true, Warning: Win32Controls.StillBusyWarning),
        _ => null,
    };

    private static ActionOutcome SetExpanded(AutomationElement element, bool expanded)
    {
        var pattern = element.Patterns.ExpandCollapse.PatternOrDefault ?? throw NotSupported(element, "expanded or collapsed (no ExpandCollapse pattern)");
        var state = pattern.ExpandCollapseState.Value;
        if (state == ExpandCollapseState.LeafNode)
            throw NotSupported(element, "expanded: it has no children");
        var target = expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        if (state == target)
            return new ActionOutcome("none", false, StateAfter: Wire(state));
        if (expanded) pattern.Expand(); else pattern.Collapse();
        return new ActionOutcome("uia.ExpandCollapsePattern", true, StateAfter: Wire(SettledState(pattern, target)));
    }

    /// <summary>
    /// Native menus open asynchronously: right after Expand() an MFC menu bar item still reads "collapsed", although
    /// the popup appears a moment later (found in the M11 agent evaluation). Wait briefly for the requested state.
    /// </summary>
    private static ExpandCollapseState SettledState(FlaUI.Core.Patterns.IExpandCollapsePattern pattern, ExpandCollapseState target)
    {
        var wait = System.Diagnostics.Stopwatch.StartNew();
        var state = pattern.ExpandCollapseState.Value;
        while (state != target && wait.Elapsed < StateSettleTime)
        {
            Thread.Sleep(25);
            state = pattern.ExpandCollapseState.Value;
        }
        return state;
    }

    private static readonly TimeSpan StateSettleTime = TimeSpan.FromMilliseconds(1000);

    /// <summary>
    /// Selects a tree node by path ("Documents > Reports > Q1.txt"), expanding each ancestor: children of collapsed
    /// nodes aren't in the UIA tree (M9), so a bare name only finds nodes that are already visible.
    /// </summary>
    private static ActionOutcome SelectTreePath(AutomationElement tree, string[] path)
    {
        var level = tree;
        AutomationElement? node = null;
        for (var i = 0; i < path.Length; i++)
        {
            var children = level.FindAllChildren(cf => cf.ByControlType(ControlType.TreeItem));
            node = children.FirstOrDefault(c => string.Equals(c.Properties.Name.ValueOrDefault, path[i], StringComparison.Ordinal))
                   ?? children.FirstOrDefault(c => string.Equals(c.Properties.Name.ValueOrDefault, path[i], StringComparison.OrdinalIgnoreCase))
                   ?? throw OptionNotFound(tree, string.Join(" > ", path[..(i + 1)]), children.Select(c => c.Properties.Name.ValueOrDefault ?? "").ToList());
            if (i < path.Length - 1 && node.Patterns.ExpandCollapse.PatternOrDefault is { } expand
                && expand.ExpandCollapseState.ValueOrDefault == ExpandCollapseState.Collapsed)
                expand.Expand();
            level = node;
        }

        var item = node!.Patterns.SelectionItem.PatternOrDefault ?? throw NotSupported(node, "selected");
        if (item.IsSelected.ValueOrDefault)
            return new ActionOutcome("none", false, ValueAfter: string.Join(" > ", path));
        item.Select();
        if (!BecameSelected(item))
            throw Ignored(node, "selection");
        return new ActionOutcome("uia.SelectionItemPattern", true, ValueAfter: string.Join(" > ", path));
    }

    private static string Wire(ExpandCollapseState state) => state switch
    {
        ExpandCollapseState.Expanded => "expanded",
        ExpandCollapseState.Collapsed => "collapsed",
        ExpandCollapseState.PartiallyExpanded => "partially_expanded",
        _ => "leaf_node",
    };

    private static ActionOutcome Focus(AutomationElement element)
    {
        if (!element.Properties.IsKeyboardFocusable.ValueOrDefault)
            throw NotSupported(element, "focused (it isn't keyboard-focusable)");
        element.Focus();
        return new ActionOutcome("uia.SetFocus", true);
    }

    private static ActionOutcome Invoke(AutomationElement element)
    {
        // Real Win32 push buttons get a posted BM_CLICK, like a user's click: the app handles it from its message loop.
        // Through InvokePattern, WinForms runs the handler *inside* the UIA call; a handler that opens a modal dialog
        // then blocks all UI Automation for that application until the dialog closes (M9 exploration), making the
        // dialog impossible to operate.
        var hwnd = element.Properties.NativeWindowHandle.ValueOrDefault;
        if (hwnd != 0 && element.Properties.ControlType.ValueOrDefault == ControlType.Button
            && ClassName(element).Contains("BUTTON", StringComparison.OrdinalIgnoreCase)
            && ClickOutcome(Win32Controls.PostClick(hwnd)) is { } clicked)
            return clicked;

        var patterns = element.Patterns;
        if (patterns.Invoke.PatternOrDefault is { } invoke)
        {
            var toolbar = ToolbarHost(element);
            var focusBefore = toolbar != 0 ? Win32Controls.FocusedWindow(toolbar) : 0;
            var tab = patterns.SelectionItem.PatternOrDefault is { } selectable && !selectable.IsSelected.ValueOrDefault ? selectable : null;
            var commandInMenu = IsCommandInOpenMenu(element);
            try
            {
                invoke.Invoke();
                if (toolbar != 0)
                    GiveFocusBack(element, toolbar, focusBefore);
            }
            catch (Exception ex) when (UiaErrors.IsTimeout(ex))
            {
                // Some providers run the click handler synchronously; a long handler or a modal dialog then outlasts
                // the UIA timeout. The click was delivered, so report success rather than inviting a double click.
                return new ActionOutcome("uia.InvokePattern", true,
                    Warning: "The application is still busy handling the click (it may have opened a modal dialog). Check with list_windows/inspect_window before acting again.");
            }
            // Some providers report success and do nothing (BCGControlBar ribbon tabs and menu items in a real-world app,
            // M11). Where the effect is observable, check it rather than claim a change.
            if (tab is not null && !BecameSelected(tab))
                return new ActionOutcome("uia.InvokePattern", false, Warning: IgnoredWarning("it is still not selected"));
            if (commandInMenu && StillShown(element))
                return new ActionOutcome("uia.InvokePattern", false, Warning: IgnoredWarning("its menu is still open, so the command probably didn't run"));
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
            if (!BecameSelected(item))
                throw Ignored(element, "selection");
            return new ActionOutcome("uia.SelectionItemPattern", true);
        }
        if (patterns.ExpandCollapse.PatternOrDefault is { } expand)
        {
            if (expand.ExpandCollapseState.Value == ExpandCollapseState.Collapsed) expand.Expand(); else expand.Collapse();
            return new ActionOutcome("uia.ExpandCollapsePattern", true);
        }
        if (hwnd != 0 && ClassName(element).Contains("BUTTON", StringComparison.OrdinalIgnoreCase)
            && ClickOutcome(Win32Controls.PostClick(hwnd)) is { } fallbackClick)
            return fallbackClick;

        throw NotSupported(element, "invoked (no Invoke, Toggle, SelectionItem or ExpandCollapse pattern, and not a Win32 button)");
    }

    /// <returns>The comctl32 toolbar window hosting a windowless toolbar button; 0 for anything else.</returns>
    private static nint ToolbarHost(AutomationElement element)
    {
        if (element.Properties.NativeWindowHandle.ValueOrDefault != 0)
            return 0;
        var parent = element.Parent;
        return parent is not null && ClassName(parent) == "ToolbarWindow32" ? parent.Properties.NativeWindowHandle.ValueOrDefault : 0;
    }

    /// <summary>
    /// Invoking a toolbar button through UIA leaves keyboard focus on the toolbar, which a mouse click never does. With
    /// focus parked there, an MFC frame's menu bar no longer opens (M10). Best effort: a command that moved focus
    /// elsewhere (e.g. opened a dialog) is left alone.
    /// </summary>
    private static void GiveFocusBack(AutomationElement element, nint toolbar, nint focusBefore)
    {
        if (focusBefore == 0 || focusBefore == toolbar || Win32Controls.FocusedWindow(toolbar) != toolbar)
            return;
        try
        {
            element.Automation.FromHandle(focusBefore).Focus();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or TimeoutException)
        {
            // The previously focused window went away or refuses focus; the click itself succeeded.
        }
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
        var isTree = container.Properties.ControlType.ValueOrDefault == ControlType.Tree;
        if (isTree && option.Contains('>'))
            return SelectTreePath(container, option.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

        // Plain Win32 combos (MFC/dialog resources): UIA's SelectionItem.Select sets the selection without sending
        // CBN_SELCHANGE, so the application never learns of it (M10). Select the way a user would be seen instead.
        var hwnd = container.Properties.NativeWindowHandle.ValueOrDefault;
        if (container.Properties.FrameworkId.ValueOrDefault == "Win32"
            && Win32Controls.ComboOptions(hwnd, ClassName(container)) is { } native
            && IndexOf(native.Options, option) >= 0)
            return SelectNativeComboItem(container, hwnd, native.Options, option);

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
                if (!BecameSelected(item))
                    throw Ignored(match, "selection");
                return new ActionOutcome("uia.SelectionItemPattern", true, ValueAfter: CurrentValue(container) ?? match.Properties.Name.ValueOrDefault);
            }

            if (items.Length == 0 && Win32Controls.ComboOptions(hwnd, ClassName(container)) is { } combo)
            {
                if (IndexOf(combo.Options, option) < 0)
                    throw OptionNotFound(container, option, combo.Options);
                return SelectNativeComboItem(container, hwnd, combo.Options, option);
            }

            if (items.Length == 0 && container.Patterns.Selection.PatternOrDefault is null && expand is null)
                throw NotSupported(container, "used to select options (it has no selectable items)");
            var notFound = OptionNotFound(container, option, items.Select(i => i.Properties.Name.ValueOrDefault ?? "").ToList());
            if (isTree)
                throw new WinMcpException(notFound.Error with
                {
                    Hint = "Children of collapsed tree nodes aren't visible. Give the full path, e.g. \"Documents > Reports > Q1.txt\", or expand nodes with set_expanded.",
                });
            throw notFound;
        }
        finally
        {
            if (wasCollapsed && expand!.ExpandCollapseState.ValueOrDefault != ExpandCollapseState.Collapsed)
                expand.Collapse();
        }
    }

    private static ActionOutcome SelectNativeComboItem(AutomationElement container, nint hwnd, IReadOnlyList<string> options, string option)
    {
        var index = IndexOf(options, option);
        if (CurrentValue(container) == options[index])
            return new ActionOutcome("none", false, ValueAfter: options[index]);
        if (!Win32Controls.SelectComboItem(hwnd, index))
            throw NotSupported(container, "changed through Win32 messages");
        return new ActionOutcome("win32.CB_SETCURSEL+CBN_SELCHANGE", true, ValueAfter: CurrentValue(container) ?? options[index]);
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

    private static bool BecameSelected(FlaUI.Core.Patterns.ISelectionItemPattern item)
    {
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while (!item.IsSelected.ValueOrDefault)
        {
            if (wait.Elapsed >= StateSettleTime)
                return false;
            Thread.Sleep(25);
        }
        return true;
    }

    /// <summary>A command item inside an open (popup) menu: invoking it should close the menu.</summary>
    private static bool IsCommandInOpenMenu(AutomationElement element)
    {
        if (element.Properties.ControlType.ValueOrDefault != ControlType.MenuItem)
            return false;
        if (element.Patterns.ExpandCollapse.PatternOrDefault is { } submenu && submenu.ExpandCollapseState.ValueOrDefault != ExpandCollapseState.LeafNode)
            return false; // opens a submenu; the menu stays open legitimately
        return element.Parent?.Properties.ControlType.ValueOrDefault == ControlType.Menu;
    }

    /// <summary>Whether the element stays on screen for a moment (gone or off-screen = the menu closed). Returns as soon as it's gone.</summary>
    private static bool StillShown(AutomationElement element)
    {
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while (wait.Elapsed < MenuCloseTime)
        {
            try
            {
                if (element.Properties.IsOffscreen.ValueOrDefault || element.Properties.BoundingRectangle.ValueOrDefault.Width <= 0)
                    return false;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or FlaUI.Core.Exceptions.ElementNotAvailableException)
            {
                return false;
            }
            Thread.Sleep(50);
        }
        return true;
    }

    private static readonly TimeSpan MenuCloseTime = TimeSpan.FromMilliseconds(500);

    private static string IgnoredWarning(string symptom) =>
        $"The application accepted the UI Automation request but nothing changed: {symptom}. Some controls (e.g. BCGControlBar ribbons and menus) ignore UI Automation; try send_keys (arrow keys, Enter, or the command's shortcut).";

    private static WinMcpException Ignored(AutomationElement element, string request) =>
        new(new WinMcpError(
            WinMcpErrorCode.PatternNotSupported,
            $"{element.Properties.ControlType.ValueOrDefault} \"{element.Properties.Name.ValueOrDefault}\" ignored the UI Automation {request}: it was accepted, but nothing changed.",
            Hint: "Some controls (e.g. BCGControlBar ribbons) ignore UI Automation. Use send_keys instead: focus a neighbouring item, then arrow keys or Enter."));

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
