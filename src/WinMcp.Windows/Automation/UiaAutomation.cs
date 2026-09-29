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
    private const int UiaTimeout = unchecked((int)0x80131505);

    public Task<RawElement> GetWindowTreeAsync(WindowHandle window, CancellationToken cancellationToken) =>
        dispatcher.RunAsync(automation => Translate(window, () => FetchTree(automation, window)), cancellationToken);

    /// <summary>
    /// One cross-process round trip for the whole control-view subtree plus every property the tree shows
    /// (M0: naive per-property walks cost one round trip per property per element).
    /// </summary>
    private static RawElement FetchTree(UIA3Automation automation, WindowHandle window)
    {
        var element = automation.PropertyLibrary.Element;
        var request = new CacheRequest
        {
            TreeScope = TreeScope.Subtree,
            TreeFilter = new NotCondition(new PropertyCondition(element.IsControlElement, false)),
        };
        foreach (var property in new PropertyId[]
        {
            element.RuntimeId, element.ControlType, element.Name, element.AutomationId, element.ClassName,
            element.NativeWindowHandle, element.BoundingRectangle, element.IsEnabled, element.IsOffscreen,
            element.IsPassword, element.HasKeyboardFocus,
            automation.PropertyLibrary.Value.Value,
            automation.PropertyLibrary.Toggle.ToggleState,
            automation.PropertyLibrary.ExpandCollapse.ExpandCollapseState,
            automation.PropertyLibrary.SelectionItem.IsSelected,
        })
        {
            request.Add(property);
        }
        foreach (var pattern in new PatternId[]
        {
            automation.PatternLibrary.ValuePattern, automation.PatternLibrary.TogglePattern,
            automation.PatternLibrary.ExpandCollapsePattern, automation.PatternLibrary.SelectionItemPattern,
        })
        {
            request.Add(pattern);
        }

        var windowElement = automation.FromHandle((nint)window.Value);
        using (request.Activate())
        {
            var root = windowElement.FindFirst(TreeScope.Element, TrueCondition.Default)
                ?? throw WindowClosed(window);
            return Build(root);
        }
    }

    private static RawElement Build(AutomationElement e)
    {
        var bounds = e.Properties.BoundingRectangle.ValueOrDefault;
        var patterns = e.Patterns;
        return new RawElement(
            RuntimeId: string.Join('.', e.Properties.RuntimeId.ValueOrDefault ?? []),
            ControlType: e.Properties.ControlType.ValueOrDefault.ToString(),
            Name: e.Properties.Name.ValueOrDefault ?? "",
            AutomationId: e.Properties.AutomationId.ValueOrDefault ?? "",
            ClassName: e.Properties.ClassName.ValueOrDefault ?? "",
            NativeWindowHandle: e.Properties.NativeWindowHandle.ValueOrDefault,
            Bounds: new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            IsEnabled: e.Properties.IsEnabled.ValueOrDefault,
            IsOffscreen: e.Properties.IsOffscreen.ValueOrDefault,
            IsPassword: e.Properties.IsPassword.ValueOrDefault,
            HasKeyboardFocus: e.Properties.HasKeyboardFocus.ValueOrDefault,
            Value: patterns.Value.PatternOrDefault?.Value.ValueOrDefault,
            ToggleState: patterns.Toggle.PatternOrDefault?.ToggleState.ValueOrDefault switch
            {
                null => null,
                ToggleState.On => "on",
                ToggleState.Off => "off",
                _ => "indeterminate",
            },
            ExpandCollapseState: patterns.ExpandCollapse.PatternOrDefault?.ExpandCollapseState.ValueOrDefault switch
            {
                null => null,
                ExpandCollapseState.Collapsed => "collapsed",
                ExpandCollapseState.Expanded => "expanded",
                ExpandCollapseState.PartiallyExpanded => "partially_expanded",
                _ => "leaf_node",
            },
            IsSelected: patterns.SelectionItem.PatternOrDefault?.IsSelected.ValueOrDefault,
            Children: e.CachedChildren.Select(Build).ToList());
    }

    /// <summary>Maps UIA failures to agent-facing errors; anything else propagates as an internal error.</summary>
    private static T Translate<T>(WindowHandle window, Func<T> work)
    {
        try
        {
            return work();
        }
        catch (Exception ex) when (ex is TimeoutException || ex.HResult == UiaTimeout)
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
