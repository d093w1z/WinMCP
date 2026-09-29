using System.Runtime.InteropServices;
using WinMcp.Core.Desktop;
using WinMcp.Core.Policy;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>Plan §E.3 tests 1–3 at the Win32 layer: discovery and window/process details of the real TestApp.</summary>
[Trait("Category", "Windows")]
public sealed class Win32DesktopTests : IDisposable
{
    private readonly TestAppSession _app = new();
    private readonly Win32Desktop _desktop = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public void Finds_the_TestApp_window_with_accurate_process_details()
    {
        var window = Assert.Single(_desktop.GetTopLevelWindows(), w => w.Process.Pid == _app.ProcessId && w.Shown);

        Assert.Equal("WinMCP Test App", window.Title);
        Assert.StartsWith("WindowsForms10.Window.", window.ClassName);
        Assert.Equal("WinMcp.TestApp", window.Process.Name);
        Assert.EndsWith(@"\WinMcp.TestApp.exe", window.Process.Path, StringComparison.OrdinalIgnoreCase);
        // TestApp is built by the same SDK as this test process, so it gets the same architecture.
        Assert.Equal(RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), window.Process.Architecture);
        Assert.False(window.Process.Elevated);
        Assert.True(window.Enabled);
        Assert.False(window.Minimized);
        Assert.Null(window.Owner);
    }

    [Fact]
    public void Bounds_are_the_visible_frame_at_the_requested_position()
    {
        var window = Assert.Single(_desktop.GetTopLevelWindows(), w => w.Process.Pid == _app.ProcessId && w.Shown);
        var uia = _app.Window.Properties.BoundingRectangle.Value;

        Assert.Equal(200, window.Bounds.Y); // --position 200,200; the visible frame starts at the requested top
        Assert.True(window.Bounds.Width > 0 && window.Bounds.Height > 0);
        // DWM visible frame sits inside the UIA rectangle, which includes invisible resize borders (M0 finding 12).
        Assert.True(window.Bounds.X >= uia.X && window.Bounds.Width <= uia.Width);
        Assert.True(window.Dpi >= 96);
    }

    [Fact]
    public void Query_with_allowlist_returns_only_the_TestApp_and_counts_the_rest()
    {
        var query = new WindowQuery(_desktop, new TargetPolicy(WinMcpOptions.Parse(["--allow", "WinMcp.TestApp"]), Environment.ProcessId));

        var result = query.List(new WindowFilter(Pid: _app.ProcessId));

        var window = Assert.Single(result.Windows);
        Assert.Equal("WinMCP Test App", window.Title);
        Assert.True(result.ExcludedCount > 0); // a real desktop always has other shown windows (e.g. the taskbar)
    }
}
