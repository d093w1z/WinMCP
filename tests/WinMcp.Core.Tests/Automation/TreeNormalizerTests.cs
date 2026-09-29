using WinMcp.Core.Automation;
using WinMcp.Testing;
using static WinMcp.Testing.FakeUiAutomation;

namespace WinMcp.Core.Tests.Automation;

public sealed class TreeNormalizerTests
{
    [Fact]
    public void Removes_title_bar_and_combo_internals()
    {
        var normalized = TreeNormalizer.Normalize(TestAppTree());

        var types = normalized.DescendantsAndSelf().Select(e => e.ControlType).ToList();
        Assert.DoesNotContain("TitleBar", types);
        Assert.DoesNotContain("MenuBar", types);
        Assert.Empty(normalized.Children.Single(e => e.AutomationId == "typeComboBox").Children);
        Assert.Equal(9, normalized.Children.Count);
    }

    [Fact]
    public void Keeps_list_items_of_an_expanded_combo()
    {
        var combo = Element("1", "ComboBox", "Type:", expand: "expanded").With(
            Element("1.1", "Button", "Close"),
            Element("1.2", "List", "Type:").With(Element("1.2.1", "ListItem", "HTML")));

        var normalized = TreeNormalizer.Normalize(combo);

        var list = Assert.Single(normalized.Children);
        Assert.Equal("ListItem", Assert.Single(list.Children).ControlType);
    }

    [Fact]
    public void Collapses_chains_of_anonymous_single_child_wrappers()
    {
        var root = Element("1", "Window", "App").With(
            Element("1.1", "Pane").With(
                Element("1.1.1", "Group").With(
                    Element("1.1.1.1", "Button", "OK"))));

        var normalized = TreeNormalizer.Normalize(root);

        Assert.Equal("OK", Assert.Single(normalized.Children).Name);
    }

    [Theory]
    [InlineData("Toolbar", "")]        // named
    [InlineData("", "panel1")]         // has an AutomationId
    public void Keeps_identifiable_wrappers(string name, string automationId)
    {
        var root = Element("1", "Window", "App").With(
            Element("1.1", "Pane", name, automationId).With(Element("1.1.1", "Button", "OK")));

        Assert.Equal("Pane", Assert.Single(TreeNormalizer.Normalize(root).Children).ControlType);
    }

    [Fact]
    public void Keeps_anonymous_panes_with_several_children()
    {
        var root = Element("1", "Window", "App").With(
            Element("1.1", "Pane").With(Element("1.1.1", "Button", "OK"), Element("1.1.2", "Button", "Cancel")));

        Assert.Equal(2, Assert.Single(TreeNormalizer.Normalize(root).Children).Children.Count);
    }
}
