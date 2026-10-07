using System.Diagnostics;
using WinMcp.Core.Automation;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>TestApp whose list view has 10,000 extra rows (~40,000 UIA elements).</summary>
public sealed class BigListSession : TestAppSession
{
    public BigListSession() : base("--rows 10000") { }
}

/// <summary>
/// M9b: WinMCP's own path on a 10,000-row list. Before M9b every call took ~5.8 s (fetch 3.2 s, right at the 3 s UIA
/// timeout, + processing 2.6 s); the bounds-free fetch and direct property reads brought it to ~2 s.
/// </summary>
[Trait("Category", "Windows")]
public sealed class LargeListTests(BigListSession app) : IClassFixture<BigListSession>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static void Log(string text) => TestContext.Current.TestOutputHelper?.WriteLine(text);

    [Fact]
    public async Task Tree_find_and_select_work_on_a_10000_row_list_within_budget()
    {
        using var s = WinMcpServices.Control();
        var hwnd = app.WindowHandle.ToString();
        await s.Tree.GetTreeAsync(hwnd, null, 10, 300, Token); // warm-up

        var sw = Stopwatch.StartNew();
        var tree = await s.Tree.GetTreeAsync(hwnd, null, 10, 300, Token);
        var treeMs = sw.ElapsedMilliseconds;
        Assert.Null(tree.Source); // UIA answered in time; no Win32 fallback
        Assert.True(tree.Truncated);

        sw.Restart();
        var late = await s.Tree.FindAsync(hwnd, null, new ElementLocator(Name: "Row 9990"), 5, Token);
        var findMs = sw.ElapsedMilliseconds;
        Assert.Equal("Row 9990 | Text | 9990 KB", Assert.Single(late.Matches).Value);

        sw.Restart();
        await s.Interaction.PerformAsync(hwnd, null, new ElementLocator(AutomationId: "itemsListView"), new ElementAction.Select("Row 9990"), Token);
        var selectMs = sw.ElapsedMilliseconds;
        TestAppSession.WaitUntil(() => app.Text("statusLabel") == "Status: Selected item: Row 9990", "the row is selected");

        Log($"10,000 rows: get_ui_tree {treeMs} ms, find_elements {findMs} ms, select_option {selectMs} ms");
        Assert.True(treeMs < 4000, $"get_ui_tree took {treeMs} ms");
        Assert.True(findMs < 4000, $"find_elements took {findMs} ms");
    }
}
