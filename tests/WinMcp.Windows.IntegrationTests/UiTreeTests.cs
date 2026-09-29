using FlaUI.Core.Definitions;
using WinMcp.Core.Automation;
using WinMcp.Core.Errors;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>Plan §E.3 tests 4–7 and 16–18 at the Core+Windows layer, against the live TestApp.</summary>
[Trait("Category", "Windows")]
public sealed class UiTreeTests : IClassFixture<TestAppSession>, IDisposable
{
    private readonly TestAppSession _app;
    private readonly WinMcpServices _services = new();

    public UiTreeTests(TestAppSession app)
    {
        _app = app;
        _app.Reset();
    }

    public void Dispose() => _services.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Hwnd => _app.WindowHandle.ToString();

    private Task<UiTree> Tree(string? element = null) =>
        _services.Tree.GetTreeAsync(element is null ? Hwnd : null, element, UiTreeService.DefaultMaxDepth, UiTreeService.DefaultMaxNodes, Token);

    private Task<ElementMatches> Find(ElementLocator locator) =>
        _services.Tree.FindAsync(Hwnd, null, locator, UiTreeService.DefaultMaxResults, Token);

    [Fact]
    public async Task Tree_exposes_the_v1_controls_semantically()
    {
        var tree = await Tree();
        var nodes = tree.Root.Children!.ToDictionary(n => n.AutomationId ?? n.Ref);

        Assert.Equal("Window", tree.Root.ControlType);
        Assert.Equal("MainForm", tree.Root.AutomationId);
        Assert.Equal(("Edit", "Name:", ""), (nodes["nameTextBox"].ControlType, nodes["nameTextBox"].Name, nodes["nameTextBox"].Value));
        Assert.Equal(("ComboBox", "Text"), (nodes["typeComboBox"].ControlType, nodes["typeComboBox"].Value));
        Assert.Null(nodes["typeComboBox"].Children); // internals normalized away
        Assert.Contains("collapsed", nodes["typeComboBox"].States!);
        Assert.Contains("on", nodes["enableCheckBox"].States!);
        Assert.Contains("disabled", nodes["advancedButton"].States!);
        Assert.Equal("Status: Ready", nodes["statusLabel"].Name);
        Assert.DoesNotContain("hiddenTextBox", nodes.Keys);
        Assert.DoesNotContain(tree.Root.Children!, n => n.ControlType == "TitleBar");
        Assert.False(tree.Truncated);
    }

    [Fact]
    public async Task Tree_reflects_live_values()
    {
        _app.SetValue("nameTextBox", "Mukesh");
        _app.SelectOption("typeComboBox", "HTML");

        var nodes = (await Tree()).Root.Children!.ToDictionary(n => n.AutomationId ?? n.Ref);

        Assert.Equal("Mukesh", nodes["nameTextBox"].Value);
        Assert.Equal("HTML", nodes["typeComboBox"].Value);
        Assert.DoesNotContain("disabled", nodes["advancedButton"].States ?? []); // enabled by name + feature
    }

    [Fact]
    public async Task Expanded_combo_shows_its_items()
    {
        var combo = _app.Find("typeComboBox").Patterns.ExpandCollapse.Pattern;
        combo.Expand();
        try
        {
            var node = (await Tree()).Root.Children!.Single(n => n.AutomationId == "typeComboBox");

            Assert.Contains("expanded", node.States!);
            var items = node.Children!.SelectMany(c => c.Children ?? []).Where(c => c.ControlType == "ListItem").Select(c => c.Name);
            Assert.Equal(["Text", "HTML", "Markdown"], items);
        }
        finally
        {
            combo.Collapse();
        }
    }

    [Fact]
    public async Task Refs_are_stable_and_shared_between_tree_and_find()
    {
        var first = await Tree();
        var second = await Tree();
        var apply = Assert.Single((await Find(new ElementLocator(AutomationId: "applyButton"))).Matches);

        Assert.Equal(first.Root.Children!.Select(n => n.Ref), second.Root.Children!.Select(n => n.Ref));
        Assert.Equal(first.Root.Children!.Single(n => n.AutomationId == "applyButton").Ref, apply.Ref);
    }

    [Fact]
    public async Task Find_by_control_type_counts_all_v1_buttons() =>
        Assert.Equal(
            ["Apply", "Cancel", "Advanced...", "Slow apply", "Add field", "Freeze 8s"],
            (await Find(new ElementLocator(ControlType: "Button"))).Matches.Select(m => m.Name));

    [Fact]
    public async Task Dynamic_controls_appear_and_their_refs_go_stale_when_removed()
    {
        _app.Invoke("addFieldButton");
        TestAppSession.WaitUntil(() => _app.TryFind("dynamicTextBox1") is not null, "dynamic field exists");

        var dynamic = Assert.Single((await Find(new ElementLocator(AutomationId: "dynamicTextBox1"))).Matches);
        Assert.Equal("Dynamic 1:", dynamic.Name);

        _app.Reset(); // removes dynamic fields
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Tree(element: dynamic.Ref));

        Assert.Equal(WinMcpErrorCode.ElementStale, ex.Error.Code);
    }

    [Fact]
    public async Task Tree_fetch_is_fast_for_the_TestApp()
    {
        await Tree(); // warm-up: first UIA connection to the process
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await Tree();

        Assert.True(stopwatch.ElapsedMilliseconds < 500, $"get_ui_tree took {stopwatch.ElapsedMilliseconds} ms (MVP target < 500 ms)");
    }
}
