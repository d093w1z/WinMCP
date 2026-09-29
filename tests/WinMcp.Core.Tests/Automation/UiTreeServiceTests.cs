using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;
using WinMcp.Core.Policy;
using WinMcp.Core.Symbols;
using WinMcp.Testing;
using static WinMcp.Testing.FakeUiAutomation;

namespace WinMcp.Core.Tests.Automation;

public sealed class UiTreeServiceTests
{
    private const string Hwnd = "hwnd:0x00000010";
    private static readonly WindowInfo Main = FakeDesktop.Window("WinMcp.TestApp", "WinMCP Test App", pid: 1, hwnd: 0x10);
    private static readonly WindowInfo Mail = FakeDesktop.Window("mail", "Inbox", pid: 2, hwnd: 0x20);

    private readonly FakeDesktop _desktop = new(Main, Mail);
    private readonly FakeUiAutomation _automation = new();
    private readonly UiTreeService _service;

    public UiTreeServiceTests()
    {
        _automation.Trees[Main.Hwnd] = TestAppTree();
        _automation.Trees[Mail.Hwnd] = Element("9", "Window", "Inbox");
        var options = new WinMcpOptions(ServerMode.Observe, ["WinMcp.TestApp"]);
        _service = new UiTreeService(new WindowQuery(_desktop, new TargetPolicy(options, ownPid: 999)), _automation, new ElementRegistry(), new SymbolProvider(options));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private Task<UiTree> Tree(string? hwnd = Hwnd, string? element = null, int maxDepth = 10, int maxNodes = 300) =>
        _service.GetTreeAsync(hwnd, element, maxDepth, maxNodes, Token);

    private Task<ElementMatches> Find(ElementLocator locator, string? element = null, int maxResults = 25) =>
        _service.FindAsync(element is null ? Hwnd : null, element, locator, maxResults, Token);

    [Fact]
    public async Task Returns_the_normalized_tree_with_values_and_states()
    {
        var tree = await Tree();

        Assert.Equal(10, tree.NodeCount);
        Assert.False(tree.Truncated);
        var nodes = tree.Root.Children!;
        var name = nodes.Single(n => n.AutomationId == "nameTextBox");
        Assert.Equal("", name.Value);
        Assert.Null(name.States);
        Assert.Equal(["collapsed"], nodes.Single(n => n.AutomationId == "typeComboBox").States);
        Assert.Equal(["on"], nodes.Single(n => n.AutomationId == "enableCheckBox").States);
        Assert.Equal(["disabled"], nodes.Single(n => n.AutomationId == "advancedButton").States);
        Assert.DoesNotContain(nodes, n => n.ControlType == "TitleBar");
    }

    [Fact]
    public async Task New_refs_follow_reading_order()
    {
        var tree = await Tree();

        Assert.Equal("e1", tree.Root.Ref);
        Assert.Equal(["e2", "e3", "e4"], tree.Root.Children!.Take(3).Select(n => n.Ref));
    }

    [Fact]
    public async Task Refs_are_stable_across_calls()
    {
        var first = await Tree();
        var second = await Tree();

        Assert.Equal(Refs(first.Root), Refs(second.Root));
        Assert.Equal(Refs(first.Root).Count, Refs(first.Root).Distinct().Count());
    }

    [Fact]
    public async Task Element_ref_alone_selects_a_subtree_and_its_window()
    {
        var combo = (await Tree()).Root.Children!.Single(n => n.AutomationId == "typeComboBox");

        var subtree = await Tree(hwnd: null, element: combo.Ref);

        Assert.Equal(combo.Ref, subtree.Root.Ref);
        Assert.Equal(Main.Hwnd, subtree.Window);
    }

    [Fact]
    public async Task Max_depth_truncates_and_reports_omitted_children()
    {
        var tree = await Tree(maxDepth: 1, maxNodes: 300);
        Assert.False(tree.Truncated); // TestApp is only one level deep after normalization

        var shallow = await Tree(maxDepth: 1, maxNodes: 4);
        Assert.True(shallow.Truncated);
        Assert.Equal("max_nodes", shallow.TruncationReason);
        Assert.Equal(4, shallow.NodeCount);
        Assert.Equal(6, shallow.Root.OmittedChildren);
    }

    [Fact]
    public async Task Max_depth_limit_is_reported_as_such()
    {
        _automation.Trees[Main.Hwnd] = Element("1", "Window", "App").With(
            Element("1.1", "Pane", "Outer").With(Element("1.1.1", "Button", "Deep")));

        var tree = await Tree(maxDepth: 1);

        Assert.Equal("max_depth", tree.TruncationReason);
        Assert.Equal(1, tree.Root.Children!.Single().OmittedChildren);
    }

    [Fact]
    public async Task Password_values_are_never_returned()
    {
        _automation.Trees[Main.Hwnd] = Element("1", "Window", "App").With(
            Element("1.1", "Edit", "Password", value: "hunter2", password: true));

        var node = (await Tree()).Root.Children!.Single();

        Assert.Null(node.Value);
        Assert.Contains("password", node.States!);
        var found = Assert.Single((await Find(new ElementLocator(ControlType: "Edit"))).Matches);
        Assert.Null(found.Value);
    }

    [Fact]
    public async Task Long_values_are_truncated()
    {
        _automation.Trees[Main.Hwnd] = Element("1", "Window", "App").With(
            Element("1.1", "Document", "Log", value: new string('x', 1000)));

        var node = (await Tree()).Root.Children!.Single();

        Assert.Equal(201, node.Value!.Length);
        Assert.EndsWith("…", node.Value);
    }

    [Fact]
    public async Task Find_matches_all_criteria_case_insensitively()
    {
        Assert.Equal("Apply", Assert.Single((await Find(new ElementLocator(AutomationId: "APPLYBUTTON"))).Matches).Name);
        Assert.Equal(3, (await Find(new ElementLocator(ControlType: "button"))).Count);
        Assert.Equal(2, (await Find(new ElementLocator(NameContains: "type"))).Count); // label + combo
        Assert.Single((await Find(new ElementLocator(NameContains: "type", ControlType: "ComboBox"))).Matches);
    }

    [Fact]
    public async Task Find_refs_match_tree_refs()
    {
        var treeRef = (await Tree()).Root.Children!.Single(n => n.AutomationId == "applyButton").Ref;

        var found = Assert.Single((await Find(new ElementLocator(AutomationId: "applyButton"))).Matches);

        Assert.Equal(treeRef, found.Ref);
    }

    [Fact]
    public async Task Find_reports_total_count_when_limited()
    {
        var result = await Find(new ElementLocator(ControlType: "Button"), maxResults: 2);

        Assert.Equal(2, result.Matches.Count);
        Assert.Equal(3, result.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task Find_does_not_match_normalized_away_noise() =>
        Assert.Equal(0, (await Find(new ElementLocator(Name: "Close"))).Count);

    [Fact]
    public async Task Find_requires_a_criterion()
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Find(new ElementLocator()));
        Assert.Equal(WinMcpErrorCode.InvalidArgument, ex.Error.Code);
    }

    [Theory]
    [InlineData("TextBox", "Did you mean 'Edit'?")]
    [InlineData("Widget", "Valid types:")]
    public async Task Unknown_control_type_gets_a_helpful_hint(string type, string hint)
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Find(new ElementLocator(ControlType: type)));

        Assert.Equal(WinMcpErrorCode.InvalidArgument, ex.Error.Code);
        Assert.Contains(hint, ex.Error.Hint);
        Assert.Equal(0, _automation.FetchCount); // validated before touching the target
    }

    [Fact]
    public async Task Element_removed_from_the_ui_is_stale()
    {
        var apply = (await Tree()).Root.Children!.Single(n => n.AutomationId == "applyButton").Ref;
        _automation.Trees[Main.Hwnd] = Element("1", "Window", "WinMCP Test App");

        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Tree(hwnd: null, element: apply));

        Assert.Equal(WinMcpErrorCode.ElementStale, ex.Error.Code);
    }

    [Fact]
    public async Task Element_and_window_must_agree()
    {
        var apply = (await Tree()).Root.Children!.Single(n => n.AutomationId == "applyButton").Ref;
        _desktop.Windows.Add(FakeDesktop.Window("WinMcp.TestApp", "Second", pid: 1, hwnd: 0x30));

        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Tree(hwnd: "hwnd:0x00000030", element: apply));

        Assert.Equal(WinMcpErrorCode.InvalidArgument, ex.Error.Code);
    }

    [Fact]
    public async Task Non_allowlisted_window_is_not_found_and_never_fetched()
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Tree(hwnd: "hwnd:0x00000020"));

        Assert.Equal(WinMcpErrorCode.WindowNotFound, ex.Error.Code);
        Assert.Equal(0, _automation.FetchCount);
    }

    [Fact]
    public async Task Timeout_on_a_responding_window_is_explained_as_a_large_tree()
    {
        _automation.ThrowOnFetch = new WinMcpException(new WinMcpError(WinMcpErrorCode.TargetNotResponding, "timed out", Hint: "hung?"));

        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Tree());

        Assert.Equal(WinMcpErrorCode.TargetNotResponding, ex.Error.Code);
        Assert.Contains("too large", ex.Error.Hint);
    }

    [Fact]
    public async Task Timeout_on_a_hung_window_keeps_the_hang_explanation()
    {
        _desktop.Details[Main.Hwnd] = FakeDesktop.DefaultDetails(Main) with { Responding = false };
        _automation.ThrowOnFetch = new WinMcpException(new WinMcpError(WinMcpErrorCode.TargetNotResponding, "timed out", Hint: "hung?"));

        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Tree());

        Assert.Equal("hung?", ex.Error.Hint);
    }

    [Theory]
    [InlineData(0, 300)]
    [InlineData(51, 300)]
    [InlineData(10, 0)]
    [InlineData(10, 2001)]
    public async Task Limits_are_validated(int maxDepth, int maxNodes)
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Tree(maxDepth: maxDepth, maxNodes: maxNodes));
        Assert.Equal(WinMcpErrorCode.InvalidArgument, ex.Error.Code);
    }

    [Fact]
    public async Task Neither_hwnd_nor_element_is_an_invalid_argument()
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Tree(hwnd: null));
        Assert.Equal(WinMcpErrorCode.InvalidArgument, ex.Error.Code);
    }

    private static List<string> Refs(UiNode node) => [node.Ref, .. (node.Children ?? []).SelectMany(Refs)];
}
