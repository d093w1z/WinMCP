namespace WinMcp.Core.Automation;

/// <summary>UI Automation control type names, as reported in trees and accepted by <c>find_elements</c>.</summary>
public static class ControlTypes
{
    public static readonly IReadOnlySet<string> All = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AppBar", "Button", "Calendar", "CheckBox", "ComboBox", "Custom", "DataGrid", "DataItem", "Document", "Edit",
        "Group", "Header", "HeaderItem", "Hyperlink", "Image", "List", "ListItem", "Menu", "MenuBar", "MenuItem",
        "Pane", "ProgressBar", "RadioButton", "ScrollBar", "SemanticZoom", "Separator", "Slider", "Spinner",
        "SplitButton", "StatusBar", "Tab", "TabItem", "Table", "Text", "Thumb", "TitleBar", "ToolBar", "ToolTip",
        "Tree", "TreeItem", "Window",
    };

    /// <summary>Names agents commonly use for other frameworks' controls.</summary>
    public static readonly IReadOnlyDictionary<string, string> CommonMistakes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["TextBox"] = "Edit",
        ["Label"] = "Text",
        ["Static"] = "Text",
        ["DropDown"] = "ComboBox",
        ["Checkbox"] = "CheckBox",
        ["Link"] = "Hyperlink",
        ["ListBox"] = "List",
        ["TreeView"] = "Tree",
        ["ListView"] = "List",
        ["Dialog"] = "Window",
    };
}
