using WinMcp.Core.Automation;

namespace WinMcp.Core.Tests.Automation;

public sealed class MenuStructureTests
{
    [Theory]
    [InlineData(new[] { "Menu", "Window" }, true)]                 // native popup menu
    [InlineData(new[] { "ToolBar", "Menu", "Window" }, true)]      // BCGControlBar application menu (M11)
    [InlineData(new[] { "Pane", "Menu" }, true)]
    [InlineData(new[] { "MenuBar", "Window" }, false)]             // top-level menu bar item: opens a menu
    [InlineData(new[] { "ToolBar", "Pane", "Window" }, false)]     // menu-item-like button on a pane's tool bar
    [InlineData(new[] { "Pane", "Window" }, true)]                 // a menu that isn't a Menu element
    public void Recognizes_commands_in_open_menus(string[] ancestors, bool expected) =>
        Assert.Equal(expected, MenuStructure.IsCommandInOpenMenu(ancestors));
}
