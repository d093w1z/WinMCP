using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Identifiers;
using FlaUI.UIA3;
using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;

namespace WinMcp.Windows.Automation;

/// <summary><see cref="IUiAutomation"/> over UIA3 (FlaUI). Every call runs on the <see cref="AutomationDispatcher"/> thread.</summary>
public sealed class UiaAutomation(AutomationDispatcher dispatcher) : IUiAutomation
{
    private const int UiaElementNotAvailable = unchecked((int)0x80040201);

    public Task<RawElement> GetWindowTreeAsync(WindowHandle window, CancellationToken cancellationToken) =>
        dispatcher.RunAsync(automation => Translate(window, () => FetchTree(automation, window)), cancellationToken);

    public Task<ElementExtras> GetElementExtrasAsync(ElementKey element, CancellationToken cancellationToken) =>
        Win32TreeBuilder.IsFallbackId(element.RuntimeId)
            ? Task.Run(() => Win32Actions.Extras(Win32TreeBuilder.HandleOf(element.RuntimeId)), cancellationToken)
            : dispatcher.RunAsync(automation => Translate(element.Window, () =>
        {
            var live = FindLive(automation, element);
            var hwnd = live.Properties.NativeWindowHandle.ValueOrDefault;
            var className = live.Properties.ClassName.ValueOrDefault ?? "";
            var labeledBy = live.Properties.LabeledBy.ValueOrDefault;
            var options = Win32Controls.ComboOptions(hwnd, className);
            return new ElementExtras(
                FrameworkId: live.Properties.FrameworkId.ValueOrDefault ?? "",
                IsKeyboardFocusable: live.Properties.IsKeyboardFocusable.ValueOrDefault,
                HelpText: live.Properties.HelpText.ValueOrDefault ?? "",
                LabeledByRuntimeId: labeledBy is null ? null : string.Join('.', labeledBy.Properties.RuntimeId.ValueOrDefault ?? []),
                Patterns: live.GetSupportedPatterns().Select(p => p.Name.Replace("Pattern", "", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList(),
                ControlId: Win32Controls.ControlId(hwnd, (nint)element.Window.Value),
                Options: options?.Options,
                OptionCount: options?.Count,
                Bounds: ToRect(live.Properties.BoundingRectangle.ValueOrDefault));
        }), cancellationToken);

    /// <summary>Elements from the Win32 fallback tree are driven by Win32 messages only — UIA is what failed for them.</summary>
    public Task<ActionOutcome> PerformAsync(ElementKey element, ElementAction action, CancellationToken cancellationToken) =>
        Win32TreeBuilder.IsFallbackId(element.RuntimeId)
            ? Task.Run(() => Win32Actions.Perform(Win32TreeBuilder.HandleOf(element.RuntimeId), action), cancellationToken)
            : dispatcher.RunAsync(automation => Translate(element.Window, () =>
        {
            var live = FindLive(automation, element);
            // Re-checked live: the element may have been disabled since the caller's check (M0: UIA acts on it anyway).
            if (!live.Properties.IsEnabled.ValueOrDefault)
                throw new WinMcpException(new WinMcpError(WinMcpErrorCode.ElementDisabled, "The element became disabled."));
            return UiaActions.Perform(automation, live, action);
        }), cancellationToken);

    /// <summary>
    /// Finds the live element with the given runtime id in one cached round trip (runtime ids only), then returns
    /// it for live property reads. Runtime ids are stable only while the element exists, so no match = stale.
    /// </summary>
    private static AutomationElement FindLive(UIA3Automation automation, ElementKey element)
    {
        var request = new CacheRequest
        {
            TreeScope = TreeScope.Subtree,
            TreeFilter = new NotCondition(new PropertyCondition(automation.PropertyLibrary.Element.IsControlElement, false)),
            // Full: the found element must support live calls afterwards; cache-only references reject them (M5).
            AutomationElementMode = AutomationElementMode.Full,
        };
        request.Add(automation.PropertyLibrary.Element.RuntimeId);

        // Get the window element before activating the cache request: with a subtree request active, FlaUI turns
        // FromHandle into ElementFromHandleBuildCache, which UIA rejects for subtree scope (COMException, found in M5).
        var windowElement = automation.FromHandle((nint)element.Window.Value);
        using (request.Activate())
        {
            var root = windowElement.FindFirst(TreeScope.Element, TrueCondition.Default)
                ?? throw WindowClosed(element.Window);
            return Find(root) ?? throw new WinMcpException(new WinMcpError(
                WinMcpErrorCode.ElementStale,
                "The element no longer exists.",
                Hint: "The UI changed. Call get_ui_tree or find_elements again for current refs."));
        }

        AutomationElement? Find(AutomationElement node)
        {
            if (string.Join('.', node.Properties.RuntimeId.ValueOrDefault ?? []) == element.RuntimeId)
                return node;
            foreach (var child in node.CachedChildren)
            {
                if (Find(child) is { } found)
                    return found;
            }
            return null;
        }
    }

    /// <summary>
    /// One cross-process round trip for the whole control-view subtree plus every property the tree shows
    /// (M0: naive per-property walks cost one round trip per property per element). Measured on a 10,000-row list
    /// (M9b): bounds cost 1.8 s of a 3.2 s fetch, so they're read live per element instead (ElementExtras.Bounds);
    /// pattern values are read as cached properties without pattern objects (processing 2.6 s → 1.2 s).
    /// </summary>
    private static RawElement FetchTree(UIA3Automation automation, WindowHandle window)
    {
        var ids = new TreeProperties(automation);
        var request = new CacheRequest
        {
            TreeScope = TreeScope.Subtree,
            TreeFilter = new NotCondition(new PropertyCondition(automation.PropertyLibrary.Element.IsControlElement, false)),
        };
        foreach (var property in ids.All)
            request.Add(property);

        var windowElement = automation.FromHandle((nint)window.Value);
        using (request.Activate())
        {
            var root = windowElement.FindFirst(TreeScope.Element, TrueCondition.Default)
                ?? throw WindowClosed(window);
            return Build(root, ids);
        }
    }

    private sealed class TreeProperties(UIA3Automation automation)
    {
        public PropertyId RuntimeId { get; } = automation.PropertyLibrary.Element.RuntimeId;
        public PropertyId ControlType { get; } = automation.PropertyLibrary.Element.ControlType;
        public PropertyId Name { get; } = automation.PropertyLibrary.Element.Name;
        public PropertyId AutomationId { get; } = automation.PropertyLibrary.Element.AutomationId;
        public PropertyId ClassName { get; } = automation.PropertyLibrary.Element.ClassName;
        public PropertyId NativeWindowHandle { get; } = automation.PropertyLibrary.Element.NativeWindowHandle;
        public PropertyId IsEnabled { get; } = automation.PropertyLibrary.Element.IsEnabled;
        public PropertyId IsOffscreen { get; } = automation.PropertyLibrary.Element.IsOffscreen;
        public PropertyId IsPassword { get; } = automation.PropertyLibrary.Element.IsPassword;
        public PropertyId HasKeyboardFocus { get; } = automation.PropertyLibrary.Element.HasKeyboardFocus;
        public PropertyId Value { get; } = automation.PropertyLibrary.Value.Value;
        public PropertyId ToggleState { get; } = automation.PropertyLibrary.Toggle.ToggleState;
        public PropertyId ExpandCollapseState { get; } = automation.PropertyLibrary.ExpandCollapse.ExpandCollapseState;
        public PropertyId IsSelected { get; } = automation.PropertyLibrary.SelectionItem.IsSelected;

        public PropertyId[] All =>
        [
            RuntimeId, ControlType, Name, AutomationId, ClassName, NativeWindowHandle, IsEnabled, IsOffscreen,
            IsPassword, HasKeyboardFocus, Value, ToggleState, ExpandCollapseState, IsSelected,
        ];
    }

    /// <summary>Reads cached values directly; an unsupported pattern property simply isn't available (→ null).</summary>
    private static RawElement Build(AutomationElement e, TreeProperties ids)
    {
        var f = e.FrameworkAutomationElement;
        T? Get<T>(PropertyId id) => f.TryGetPropertyValue<T>(id, out var value) ? value : default;
        T? GetStruct<T>(PropertyId id) where T : struct => f.TryGetPropertyValue<T>(id, out var value) ? value : null;

        return new RawElement(
            RuntimeId: string.Join('.', Get<int[]>(ids.RuntimeId) ?? []),
            ControlType: Get<ControlType>(ids.ControlType).ToString(),
            Name: Get<string>(ids.Name) ?? "",
            AutomationId: Get<string>(ids.AutomationId) ?? "",
            ClassName: Get<string>(ids.ClassName) ?? "",
            NativeWindowHandle: Get<nint>(ids.NativeWindowHandle),
            Bounds: default,
            IsEnabled: Get<bool>(ids.IsEnabled),
            IsOffscreen: Get<bool>(ids.IsOffscreen),
            IsPassword: Get<bool>(ids.IsPassword),
            HasKeyboardFocus: Get<bool>(ids.HasKeyboardFocus),
            Value: Get<string>(ids.Value),
            ToggleState: GetStruct<ToggleState>(ids.ToggleState) switch
            {
                null => null,
                ToggleState.On => "on",
                ToggleState.Off => "off",
                _ => "indeterminate",
            },
            ExpandCollapseState: GetStruct<ExpandCollapseState>(ids.ExpandCollapseState) switch
            {
                null => null,
                ExpandCollapseState.Collapsed => "collapsed",
                ExpandCollapseState.Expanded => "expanded",
                ExpandCollapseState.PartiallyExpanded => "partially_expanded",
                _ => "leaf_node",
            },
            IsSelected: GetStruct<bool>(ids.IsSelected),
            Children: e.CachedChildren.Select(c => Build(c, ids)).ToList());
    }

    private static Rect ToRect(System.Drawing.Rectangle r) => new(r.X, r.Y, r.Width, r.Height);

    /// <summary>Maps UIA failures to agent-facing errors; anything else propagates as an internal error.</summary>
    private static T Translate<T>(WindowHandle window, Func<T> work)
    {
        try
        {
            return work();
        }
        catch (Exception ex) when (UiaErrors.IsTimeout(ex))
        {
            throw new WinMcpException(new WinMcpError(
                WinMcpErrorCode.TargetNotResponding,
                $"Window {window} did not answer UI Automation in time.",
                Hint: "The application may be busy or hung (check inspect_window 'responding'). Retry later."), ex);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException || ex.HResult == UiaElementNotAvailable || ex is COMException { HResult: unchecked((int)0x80070578) })
        {
            // 0x80070578 = ERROR_INVALID_WINDOW_HANDLE
            throw WindowClosed(window, ex);
        }
    }

    private static WinMcpException WindowClosed(WindowHandle window, Exception? inner = null) =>
        new(new WinMcpError(
            WinMcpErrorCode.WindowClosed,
            $"Window {window} closed or became unavailable.",
            Hint: "Call list_windows for current windows."), inner);
}
