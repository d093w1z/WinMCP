using WinMcp.Core.Desktop;

namespace WinMcp.Core.Automation;

/// <summary>Identifies a UI Automation element: its top-level window plus its UIA runtime id (e.g. <c>42.3345.4.2</c>).</summary>
public readonly record struct ElementKey(WindowHandle Window, string RuntimeId);

/// <summary>
/// One UI Automation element as fetched from the target, before any normalization. <see cref="Bounds"/> is empty for
/// UIA tree fetches (too costly to fetch in bulk; use <see cref="ElementExtras.Bounds"/>) and set for Win32 fallback trees. String enums use UIA names:
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

/// <summary>Per-element facts too costly to fetch for every node of a tree; read live for one element.</summary>
/// <param name="Patterns">Supported UIA control patterns by short name, e.g. <c>Value</c>, <c>Invoke</c>.</param>
/// <param name="ControlId">Win32 control ID (<c>GetDlgCtrlID</c>) when the element is itself a child window.</param>
/// <param name="Options">Items of a combo box read without opening it (possibly capped); null when not a combo or not readable.</param>
/// <param name="OptionCount">Total number of items, which may exceed <see cref="Options"/>' length.</param>
/// <param name="Bounds">Screen rectangle, read live: bulk tree fetches omit bounds (M9b: 1.8 s of 3.2 s on a 10,000-row list).</param>
public sealed record ElementExtras(
    string FrameworkId,
    bool IsKeyboardFocusable,
    string HelpText,
    string? LabeledByRuntimeId,
    IReadOnlyList<string> Patterns,
    int? ControlId,
    IReadOnlyList<string>? Options,
    int? OptionCount = null,
    Rect Bounds = default)
{
    /// <summary>Implementations return at most this many <see cref="Options"/>.</summary>
    public const int MaxOptions = 200;
}

public interface IUiAutomation
{
    /// <summary>
    /// The control-view subtree of a top-level window. Throws <see cref="Errors.WinMcpException"/> with
    /// TARGET_NOT_RESPONDING or WINDOW_CLOSED when the target can't answer.
    /// </summary>
    Task<RawElement> GetWindowTreeAsync(WindowHandle window, CancellationToken cancellationToken);

    /// <summary>Live details of one element. Throws ELEMENT_STALE when it no longer exists.</summary>
    Task<ElementExtras> GetElementExtrasAsync(ElementKey element, CancellationToken cancellationToken);

    /// <summary>
    /// Performs an action. Policy checks (allowlist, enabled, password) are the caller's job; implementations still
    /// throw ELEMENT_DISABLED if the element became disabled meanwhile, PATTERN_NOT_SUPPORTED when no mechanism
    /// applies, OPTION_NOT_FOUND for unknown options, and ELEMENT_STALE when the element is gone.
    /// </summary>
    Task<ActionOutcome> PerformAsync(ElementKey element, ElementAction action, CancellationToken cancellationToken);
}
