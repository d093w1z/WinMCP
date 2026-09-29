using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using WinMcp.Core.Automation;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>
/// Measures the M0 open question on a ~630-node tree: naive per-property walk vs the single cached fetch WinMCP uses.
/// Timings go to the test output. M4 result: cached ~1.5 s vs naive ~2.0 s, and a cache request with no properties
/// at all costs about the same, i.e. the target's own tree traversal (~2.5 ms/element for WinForms) dominates.
/// Only a generous sanity bound is asserted, because the margin is too small to assert reliably.
/// </summary>
[Trait("Category", "Windows")]
public sealed class TreeSizeBenchmark(StressTestAppSession app) : IClassFixture<StressTestAppSession>, IDisposable
{
    private readonly WinMcpServices _services = new();

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task Large_tree_fetch_completes_within_the_uia_timeout()
    {
        var token = TestContext.Current.CancellationToken;
        await _services.Automation.GetWindowTreeAsync(app.WindowHandle, token); // warm-up

        var naive = Median(() => NaiveCount(app.Window, app.Automation.TreeWalkerFactory.GetControlViewWalker()), out var naiveNodes);
        var cached = await MedianAsync(async () => (await _services.Automation.GetWindowTreeAsync(app.WindowHandle, token)).DescendantsAndSelf().Count());

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{naiveNodes} nodes: naive walk median {naive.Median:F0} ms, cached fetch median {cached.Median:F0} ms (x{naive.Median / cached.Median:F1})");
        Assert.True(cached.Nodes >= StressTestAppSession.ButtonCount, $"expected ≥ {StressTestAppSession.ButtonCount} nodes, got {cached.Nodes}");
        Assert.True(cached.Median < 3000, $"cached fetch took {cached.Median:F0} ms; the 3 s UIA timeout would misreport this as a hang");
    }

    [Fact]
    public async Task Default_node_limit_truncates_a_large_tree()
    {
        var tree = await _services.Tree.GetTreeAsync(app.WindowHandle.ToString(), null, UiTreeService.DefaultMaxDepth, UiTreeService.DefaultMaxNodes, TestContext.Current.CancellationToken);

        Assert.True(tree.Truncated);
        Assert.Equal("max_nodes", tree.TruncationReason);
        Assert.Equal(UiTreeService.DefaultMaxNodes, tree.NodeCount);
    }

    private static int NaiveCount(AutomationElement element, FlaUI.Core.ITreeWalker walker)
    {
        // Same property reads the cached fetch makes, one cross-process call each (M0's naive strategy).
        _ = element.Properties.Name.ValueOrDefault;
        _ = element.Properties.ControlType.ValueOrDefault;
        _ = element.Properties.AutomationId.ValueOrDefault;
        _ = element.Properties.IsEnabled.ValueOrDefault;
        _ = element.Properties.IsOffscreen.ValueOrDefault;
        _ = element.Properties.BoundingRectangle.ValueOrDefault;
        var count = 1;
        for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
            count += NaiveCount(child, walker);
        return count;
    }

    private static (double Median, int Nodes) Median(Func<int> run, out int nodes)
    {
        var times = new List<double>();
        nodes = 0;
        for (var i = 0; i < 3; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            nodes = run();
            times.Add(stopwatch.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        return (times[1], nodes);
    }

    private static async Task<(double Median, int Nodes)> MedianAsync(Func<Task<int>> run)
    {
        var times = new List<double>();
        var nodes = 0;
        for (var i = 0; i < 3; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            nodes = await run();
            times.Add(stopwatch.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        return (times[1], nodes);
    }
}
