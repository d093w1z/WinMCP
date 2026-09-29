using WinMcp.Core.Desktop;

namespace WinMcp.Core.Automation;

/// <param name="States">
/// Only non-default facts: <c>disabled</c>, <c>offscreen</c>, <c>focused</c>, <c>password</c>,
/// <c>collapsed|expanded|partially_expanded</c>, <c>on|off|indeterminate</c>, <c>selected</c>.
/// </param>
/// <param name="OmittedChildren">Children not included because of depth/node limits; drill down with this node's ref.</param>
/// <param name="ControlSymbol"><c>resource.h</c> name of a numeric AutomationId (Win32 control ID), when configured and unambiguous.</param>
public sealed record UiNode(
    string Ref,
    string ControlType,
    string Name,
    string? AutomationId,
    string? Value,
    IReadOnlyList<string>? States,
    IReadOnlyList<UiNode>? Children,
    int? OmittedChildren,
    string? ControlSymbol = null);

/// <param name="TruncationReason"><c>max_depth</c> or <c>max_nodes</c> (the first limit hit).</param>
public sealed record UiTree(WindowHandle Window, UiNode Root, int NodeCount, bool Truncated, string? TruncationReason);

public sealed record ElementMatch(string Ref, string ControlType, string Name, string? AutomationId, string? Value, IReadOnlyList<string>? States);

/// <param name="Count">Total matches; <see cref="Matches"/> holds at most max_results of them.</param>
public sealed record ElementMatches(IReadOnlyList<ElementMatch> Matches, int Count, bool Truncated);

/// <summary>AND-ed criteria for finding elements. String comparisons are case-insensitive.</summary>
/// <param name="ControlSymbol"><c>resource.h</c> name such as <c>IDC_EDIT_NAME</c>; needs <c>--symbols</c> for the process.</param>
public sealed record ElementLocator(
    string? AutomationId = null,
    string? Name = null,
    string? NameContains = null,
    string? ControlType = null,
    string? ClassName = null,
    string? ControlSymbol = null)
{
    public bool IsEmpty =>
        AutomationId is null && Name is null && NameContains is null && ControlType is null && ClassName is null && ControlSymbol is null;
}

/// <summary>A durable way to find an element again (unlike refs, valid across sessions), checked against the current UI.</summary>
/// <param name="Unique">Whether it matches exactly one element right now. When false, prefer the ref.</param>
public sealed record SuggestedLocator(string? AutomationId, string? ControlSymbol, string? Name, string? ControlType, bool Unique);

/// <summary>Agent-facing result of <c>inspect_element</c>.</summary>
/// <param name="Hwnd">The element's own window handle; null for windowless elements (e.g. list items).</param>
/// <param name="HostHwnd">Nearest window handle at or above the element.</param>
/// <param name="ControlSymbolCandidates">Several <c>resource.h</c> names share this control ID; none is chosen.</param>
/// <param name="OptionCount">Total combo items; when larger than <see cref="Options"/>' length, the list was capped.</param>
public sealed record ElementDetail(
    string Ref,
    string ControlType,
    string Name,
    string? AutomationId,
    string? Value,
    IReadOnlyList<string>? States,
    string ClassName,
    string? FrameworkId,
    WindowHandle? Hwnd,
    WindowHandle HostHwnd,
    int? ControlId,
    string? ControlSymbol,
    IReadOnlyList<string>? ControlSymbolCandidates,
    bool Enabled,
    bool Offscreen,
    bool Focusable,
    bool HasFocus,
    IReadOnlyList<string> Patterns,
    IReadOnlyList<string>? Options,
    int? OptionCount,
    Rect Bounds,
    string? Parent,
    string? LabeledBy,
    string? HelpText,
    SuggestedLocator Locator);
