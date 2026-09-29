using WinMcp.Core.Desktop;

namespace WinMcp.Core.Automation;

/// <summary>Identifies a UI Automation element: its top-level window plus its UIA runtime id (e.g. <c>42.3345.4.2</c>).</summary>
public readonly record struct ElementKey(WindowHandle Window, string RuntimeId);

/// <summary>
/// One UI Automation element as fetched from the target, before any normalization. String enums use UIA names:
/// <see cref="ControlType"/> like <c>Button</c>; <see cref="ToggleState"/> <c>on|off|indeterminate</c>;
/// <see cref="ExpandCollapseState"/> <c>collapsed|expanded|partially_expanded|leaf_node</c>.
/// Pattern-derived values are null when the element doesn't support the pattern.
/// </summary>
public sealed record RawElement(
    string RuntimeId,
    string ControlType,
    string Name,
    string AutomationId,
    string ClassName,
    long NativeWindowHandle,
    Rect Bounds,
    bool IsEnabled,
    bool IsOffscreen,
    bool IsPassword,
    bool HasKeyboardFocus,
    string? Value,
    string? ToggleState,
    string? ExpandCollapseState,
    bool? IsSelected,
    IReadOnlyList<RawElement> Children)
{
    public IEnumerable<RawElement> DescendantsAndSelf() => Children.SelectMany(c => c.DescendantsAndSelf()).Prepend(this);
}

public interface IUiAutomation
{
    /// <summary>
    /// The control-view subtree of a top-level window. Throws <see cref="Errors.WinMcpException"/> with
    /// TARGET_NOT_RESPONDING or WINDOW_CLOSED when the target can't answer.
    /// </summary>
    Task<RawElement> GetWindowTreeAsync(WindowHandle window, CancellationToken cancellationToken);
}
