namespace WinMcp.Core.Automation;

/// <summary>Where a menu item sits, from the UIA control types above it.</summary>
public static class MenuStructure
{
    /// <summary>
    /// Whether a <c>MenuItem</c> is a command in an open menu, so invoking it should close the menu. Any <c>Menu</c>
    /// ancestor decides: BCGControlBar's application menu nests its items in a tool bar inside the popup
    /// (<c>Menu → ToolBar → MenuItem</c>, M11). Without one, items directly on a menu bar or tool bar are commands of
    /// those bars (they open menus or act in place), anything else counts as in a menu.
    /// </summary>
    /// <param name="ancestorControlTypes">UIA control types from the parent upwards, e.g. <c>["ToolBar", "Menu", "Window"]</c>.</param>
    public static bool IsCommandInOpenMenu(IReadOnlyList<string> ancestorControlTypes) =>
        ancestorControlTypes.Contains("Menu")
        || ancestorControlTypes.FirstOrDefault() is not ("MenuBar" or "ToolBar");
}
