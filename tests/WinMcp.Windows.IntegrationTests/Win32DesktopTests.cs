using System.Runtime.InteropServices;
using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;
using WinMcp.Core.Policy;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>Plan §E.3 tests 1–3 at the Win32 layer: discovery and window/process details of the real TestApp.</summary>
[Trait("Category", "Windows")]
public sealed class Win32DesktopTests : IClassFixture<TestAppSession>
{
    private readonly TestAppSession _app;
    private readonly Win32Desktop _desktop = new();
    private readonly WindowQuery _query;

    public Win32DesktopTests(TestAppSession app)
    {
        _app = app;
        _app.Reset();
        _query = new WindowQuery(_desktop, new TargetPolicy(WinMcpOptions.Parse(["--allow", "WinMcp.TestApp"]), Environment.ProcessId));
    }

    // With UAC, an administrator's token only holds the Administrators group when elevated.
    private static bool RunningElevated =>
        new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

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
        // The app inherits this process's token: not elevated on a normal desktop, elevated on CI runners
        // (hosted runners run as administrator with UAC off).
        Assert.Equal(RunningElevated, window.Process.Elevated);
        Assert.True(window.Enabled);
        Assert.False(window.Minimized);
        Assert.Null(window.Owner);
    }

    [Fact]
    public async Task Bounds_are_the_visible_frame_at_the_requested_position()
    {
        var window = Assert.Single(_desktop.GetTopLevelWindows(), w => w.Process.Pid == _app.ProcessId && w.Shown);
        // WinMCP's own UIA reading, not the test harness's: the harness runs DPI-unaware and gets scaled coordinates
        // above 100% scaling, while WinMCP reads everything in physical pixels (M8).
        using var services = new WinMcpServices();
        var root = (await services.Tree.GetTreeAsync(window.Hwnd.ToString(), null, 1, 1, TestContext.Current.CancellationToken)).Root;
        // Trees don't carry bounds (M9b); inspect_element reads them live.
        var uia = (await services.Tree.InspectAsync(null, root.Ref, new WinMcp.Core.Automation.ElementLocator(), TestContext.Current.CancellationToken)).Bounds;

        Assert.Equal(200, window.Bounds.Y); // --position 200,200; the visible frame starts at the requested top
        Assert.True(window.Bounds.Width > 0 && window.Bounds.Height > 0);
        // DWM visible frame sits inside the UIA rectangle, which includes invisible resize borders (M0 finding 12).
        Assert.True(window.Bounds.X >= uia.X && window.Bounds.Width <= uia.Width, $"DWM {window.Bounds} vs UIA {uia}");
        Assert.True(uia.Width - window.Bounds.Width <= 40 * window.Dpi / 96, $"DWM {window.Bounds} vs UIA {uia}: more than resize borders apart");
        Assert.True(window.Dpi >= 96);
        TestContext.Current.TestOutputHelper?.WriteLine($"dpi {window.Dpi}: DWM {window.Bounds}, UIA {uia}");
    }

    [Fact]
    public void Query_with_allowlist_returns_only_the_TestApp_and_counts_the_rest()
    {
        var result = _query.List(new WindowFilter(Pid: _app.ProcessId));

        var window = Assert.Single(result.Windows);
        Assert.Equal("WinMCP Test App", window.Title);
        Assert.True(result.ExcludedCount > 0); // a real desktop always has other shown windows (e.g. the taskbar)
    }

    [Fact]
    public void Inspect_reports_the_TestApp_frame_styles_framework_and_children()
    {
        var inspection = _query.Inspect(MainWindowHandle());

        Assert.True(inspection.Responding);
        Assert.Equal("winforms", inspection.FrameworkHint);
        // FormBorderStyle.FixedSingle + MaximizeBox = false
        Assert.Contains("WS_CAPTION", inspection.Styles);
        Assert.Contains("WS_SYSMENU", inspection.Styles);
        Assert.Contains("WS_MINIMIZEBOX", inspection.Styles);
        Assert.DoesNotContain("WS_MAXIMIZEBOX", inspection.Styles);
        Assert.DoesNotContain("WS_THICKFRAME", inspection.Styles);
        Assert.Empty(inspection.OwnedWindows);
        Assert.True(inspection.ChildWindows.Count >= 12, $"expected the v1 controls as child windows, got {inspection.ChildWindows.Count}");
        Assert.Contains(inspection.ChildWindows.ByClass.Keys, c => c.StartsWith("WindowsForms10.BUTTON", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Inspect_sees_dynamically_created_child_windows()
    {
        var before = _query.Inspect(MainWindowHandle()).ChildWindows.Count;

        _app.Invoke("addFieldButton");
        TestAppSession.WaitUntil(() => _app.TryFind("dynamicTextBox1") is not null, "dynamic field exists");

        Assert.Equal(before + 2, _query.Inspect(MainWindowHandle()).ChildWindows.Count); // label + text box
    }

    [Fact]
    public void Inspect_rejects_a_control_handle()
    {
        var control = new WindowHandle(_app.Find("nameTextBox").Properties.NativeWindowHandle.Value);

        var ex = Assert.Throws<WinMcpException>(() => _query.Inspect(control.ToString()));

        Assert.Equal(WinMcpErrorCode.InvalidArgument, ex.Error.Code);
    }

    private string MainWindowHandle() =>
        new WindowHandle(_app.Window.Properties.NativeWindowHandle.Value).ToString();
}
