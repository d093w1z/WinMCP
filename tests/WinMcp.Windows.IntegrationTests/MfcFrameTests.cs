using System.Diagnostics;
using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>The MFC test app's <c>--frame</c> mode: CFrameWnd + CView + CToolBar + CStatusBar.</summary>
public sealed class MfcFrameSession : IDisposable
{
    private readonly Process? _process;

    public MfcFrameSession() => (_process, Handle, SkipReason) = MfcAppUnderTest.Launch("--frame", "WinMCP MFC Frame App");

    public string? SkipReason { get; }

    public WindowHandle Handle { get; }

    public void Dispose()
    {
        if (_process is { HasExited: false })
            _process.Kill();
        _process?.Dispose();
    }
}

/// <summary>M10: what UI Automation makes of classic MFC frame parts (toolbar, status bar, view).</summary>
[Trait("Category", "Windows")]
public sealed class MfcFrameTests : IClassFixture<MfcFrameSession>, IDisposable
{
    private readonly MfcFrameSession _app;
    private readonly WinMcpServices _services = new(
        "--mode", "control", "--allow", "WinMcp.MfcTestApp", "--audit-dir", Path.Combine(Path.GetTempPath(), $"winmcp-audit-{Guid.NewGuid():N}"));

    public MfcFrameTests(MfcFrameSession app) => _app = app;

    public void Dispose() => _services.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Hwnd => _app.Handle.ToString();

    // Per test, not in the constructor: a skip thrown from a constructor counts as a failure.
    private void RequireApp() => Assert.SkipWhen(_app.SkipReason is not null, _app.SkipReason ?? "");

    private Task<ActionResult> Act(ElementLocator locator, ElementAction action) =>
        _services.Interaction.PerformAsync(Hwnd, null, locator, action, Token);

    private Task WaitCount(int count) =>
        _services.Tree.WaitAsync(Hwnd, null, new ElementLocator(NameContains: "Count: ", ControlType: "Text"), WaitCondition.TextEquals, $"Count: {count}", 5000, Token);

    [Fact]
    public async Task Frame_parts_are_exposed_semantically()
    {
        RequireApp();
        var outline = OutlineRenderer.Render(await _services.Tree.GetTreeAsync(Hwnd, null, 12, 400, Token));

        Assert.Contains("MenuItem \"File\"", outline);
        Assert.Contains("ToolBar", outline);
        Assert.Contains("Button \"New\"", outline);   // toolbar button names come from MFC's "prompt\ntip" strings
        Assert.Contains("Button \"Count\"", outline);
        Assert.Contains("StatusBar", outline);
        Assert.Contains("\"Count: 0\"", outline);    // status-bar pane kept current by ON_UPDATE_COMMAND_UI
    }

    [Fact]
    public async Task Toolbar_buttons_and_menu_commands_drive_the_app()
    {
        RequireApp();
        await Act(new ElementLocator(Name: "New", ControlType: "Button"), new ElementAction.Invoke());
        await WaitCount(0);

        await Act(new ElementLocator(Name: "Count", ControlType: "Button"), new ElementAction.Invoke());
        await Act(new ElementLocator(Name: "Count", ControlType: "Button"), new ElementAction.Invoke());
        await WaitCount(2);

        await Act(new ElementLocator(Name: "File", ControlType: "MenuItem"), new ElementAction.Invoke());
        await _services.Tree.WaitAsync(Hwnd, null, new ElementLocator(Name: "New", ControlType: "MenuItem"), WaitCondition.Exists, null, 3000, Token);
        await Act(new ElementLocator(Name: "New", ControlType: "MenuItem"), new ElementAction.Invoke());
        await WaitCount(0);
    }
}
