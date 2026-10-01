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
        Assert.Contains("Text \"Count: ", outline);  // status-bar pane kept current by ON_UPDATE_COMMAND_UI
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

    [Fact]
    public async Task Frame_parts_carry_MFC_symbols_and_class_guesses_without_configuration()
    {
        RequireApp();

        var outline = OutlineRenderer.Render(await _services.Tree.GetTreeAsync(Hwnd, null, 12, 400, Token));
        async Task<string?> Guess(string symbol) => (await _services.Tree.InspectAsync(Hwnd, null, new ElementLocator(ControlSymbol: symbol), Token)).MfcClassGuess;

        Assert.Contains("#59648 (AFX_IDW_PANE_FIRST)", outline);
        Assert.Contains("ToolBar \"\" #59392 (AFX_IDW_TOOLBAR)", outline);
        Assert.Contains("StatusBar \"\" #59393 (AFX_IDW_STATUS_BAR)", outline);
        Assert.Equal("CView", await Guess("AFX_IDW_PANE_FIRST"));
        Assert.Equal("CToolBar", await Guess("AFX_IDW_TOOLBAR"));
        Assert.Equal("CStatusBar", await Guess("AFX_IDW_STATUS_BAR"));
        Assert.Equal("CFrameWnd", (await _services.Tree.InspectAsync(Hwnd, null, new ElementLocator(ControlType: "Window"), Token)).MfcClassGuess);
    }

    [Fact]
    public async Task Standard_MFC_commands_can_be_located_by_symbol()
    {
        RequireApp();
        var file = new ElementLocator(Name: "File", ControlType: "MenuItem");
        var expand = await Act(file, new ElementAction.SetExpanded(true));
        try
        {
            Assert.Equal("expanded", expand.StateAfter); // native menus open asynchronously; read back too early it said "collapsed"

            var exit = await _services.Tree.WaitAsync(Hwnd, null, new ElementLocator(ControlSymbol: "ID_APP_EXIT"), WaitCondition.Exists, null, 3000, Token);

            Assert.Equal("Exit", exit.Element!.Name);
            Assert.Equal("57665", exit.Element.AutomationId);
        }
        finally
        {
            await Act(file, new ElementAction.SetExpanded(false));
        }
    }
}
