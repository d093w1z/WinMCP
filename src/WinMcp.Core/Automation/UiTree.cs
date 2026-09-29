using WinMcp.Core.Desktop;

namespace WinMcp.Core.Automation;

/// <param name="States">
/// Only non-default facts: <c>disabled</c>, <c>offscreen</c>, <c>focused</c>, <c>password</c>,
/// <c>collapsed|expanded|partially_expanded</c>, <c>on|off|indeterminate</c>, <c>selected</c>.
/// </param>
/// <param name="OmittedChildren">Children not included because of depth/node limits; drill down with this node's ref.</param>
public sealed record UiNode(
    string Ref,
    string ControlType,
    string Name,
    string? AutomationId,
    string? Value,
    IReadOnlyList<string>? States,
    IReadOnlyList<UiNode>? Children,
    int? OmittedChildren);

/// <param name="TruncationReason"><c>max_depth</c> or <c>max_nodes</c> (the first limit hit).</param>
public sealed record UiTree(WindowHandle Window, UiNode Root, int NodeCount, bool Truncated, string? TruncationReason);

public sealed record ElementMatch(string Ref, string ControlType, string Name, string? AutomationId, string? Value, IReadOnlyList<string>? States);

/// <param name="Count">Total matches; <see cref="Matches"/> holds at most max_results of them.</param>
public sealed record ElementMatches(IReadOnlyList<ElementMatch> Matches, int Count, bool Truncated);

/// <summary>AND-ed criteria for <c>find_elements</c>. String comparisons are case-insensitive.</summary>
public sealed record ElementLocator(
    string? AutomationId = null,
    string? Name = null,
    string? NameContains = null,
    string? ControlType = null,
    string? ClassName = null)
{
    public bool IsEmpty => AutomationId is null && Name is null && NameContains is null && ControlType is null && ClassName is null;
}
