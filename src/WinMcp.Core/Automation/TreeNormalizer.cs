namespace WinMcp.Core.Automation;

/// <summary>
/// Removes UIA noise so trees are smaller and closer to what a user sees (plan §C.3, M0 findings 6 and 11):
/// title bars, combo box internals, and anonymous single-child wrapper panes.
/// </summary>
public static class TreeNormalizer
{
    public static RawElement Normalize(RawElement root) => root with { Children = NormalizeChildren(root) };

    private static List<RawElement> NormalizeChildren(RawElement parent)
    {
        var children = new List<RawElement>(parent.Children.Count);
        foreach (var child in parent.Children)
        {
            if (IsNoise(parent, child))
                continue;

            var normalized = Normalize(child);
            while (IsAnonymousWrapper(normalized))
                normalized = normalized.Children[0];
            children.Add(FoldRowCells(normalized));
        }
        return children;
    }

    private static bool IsNoise(RawElement parent, RawElement child) =>
        // System menu and min/max/close buttons: window chrome, not application UI.
        child.ControlType == "TitleBar"
        // A combo's inner text and drop-down button duplicate the combo's own Value and ExpandCollapse patterns.
        || (parent.ControlType == "ComboBox" && child.ControlType is "Button" or "Text");

    /// <summary>
    /// A list-view row exposes one Text child per column (M9: 4 nodes per row). Folding them into the row's value
    /// ("Beta | HTML | 2 KB") keeps the information and makes list trees about 4× smaller.
    /// </summary>
    private static RawElement FoldRowCells(RawElement row) =>
        row.ControlType is "ListItem" or "DataItem"
        && row.Children.Count > 0
        && row.Value is null
        && row.Children.All(c => c.ControlType == "Text" && c.Children.Count == 0)
            ? row with { Value = string.Join(" | ", row.Children.Select(c => c.Name)), Children = [] }
            : row;

    private static bool IsAnonymousWrapper(RawElement element) =>
        element.ControlType is "Pane" or "Group" or "Custom"
        && element.Name.Length == 0
        && element.AutomationId.Length == 0
        && element.Children.Count == 1;
}
