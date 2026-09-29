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
            children.Add(normalized);
        }
        return children;
    }

    private static bool IsNoise(RawElement parent, RawElement child) =>
        // System menu and min/max/close buttons: window chrome, not application UI.
        child.ControlType == "TitleBar"
        // A combo's inner text and drop-down button duplicate the combo's own Value and ExpandCollapse patterns.
        || (parent.ControlType == "ComboBox" && child.ControlType is "Button" or "Text");

    private static bool IsAnonymousWrapper(RawElement element) =>
        element.ControlType is "Pane" or "Group" or "Custom"
        && element.Name.Length == 0
        && element.AutomationId.Length == 0
        && element.Children.Count == 1;
}
