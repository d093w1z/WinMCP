using WinMcp.Core.Automation;
using WinMcp.Core.Capture;
using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;
using WinMcp.Core.Policy;
using WinMcp.Core.Symbols;
using WinMcp.Core.Tests.Automation;
using WinMcp.Testing;
using static WinMcp.Testing.FakeUiAutomation;

namespace WinMcp.Core.Tests.Desktop;

/// <summary>Elevated targets: window facts stay available, reading or operating the UI is refused clearly.</summary>
public sealed class ElevatedTargetTests
{
    private const string Hwnd = "hwnd:0x00000010";
    private static readonly WindowInfo Admin = FakeDesktop.Window("AdminTool", "Admin Tool", pid: 1, hwnd: 0x10) is var w
        ? w with { Process = w.Process with { Elevated = true } }
        : null!;

    private readonly FakeDesktop _desktop = new(Admin);
    private readonly FakeUiAutomation _automation = new();

    public ElevatedTargetTests() =>
        _automation.Trees[Admin.Hwnd] = Element("1", "Window", "Admin Tool").With(Element("1.1", "Button", "Apply", "applyButton"));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private (WindowQuery Windows, UiTreeService Tree, InteractionService Interaction, ScreenshotService Screenshots) Services()
    {
        var options = new WinMcpOptions(ServerMode.Control, ["AdminTool"]);
        var windows = new WindowQuery(_desktop, new TargetPolicy(options, 999));
        var tree = new UiTreeService(windows, _automation, new ElementRegistry(), new SymbolProvider(options));
        return (windows, tree, new InteractionService(tree, _automation, new FakeKeyboard(), windows, options, new MemoryAuditLog()),
            new ScreenshotService(windows, tree, new FakeScreenCapture()));
    }

    private static async Task AssertElevated(Func<Task> call)
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(call);
        Assert.Equal(WinMcpErrorCode.AccessDeniedElevated, ex.Error.Code);
        Assert.Contains("runs elevated", ex.Error.Message);
        Assert.False(ex.Error.Retryable);
    }

    [Fact]
    public async Task Reading_or_operating_the_UI_of_an_elevated_application_is_refused()
    {
        var (_, tree, interaction, screenshots) = Services();

        await AssertElevated(() => tree.GetTreeAsync(Hwnd, null, 10, 100, Token));
        await AssertElevated(() => tree.FindAsync(Hwnd, null, new ElementLocator(Name: "Apply"), 5, Token));
        await AssertElevated(() => tree.WaitAsync(Hwnd, null, new ElementLocator(Name: "Apply"), WaitCondition.Exists, null, 0, Token));
        await AssertElevated(() => interaction.PerformAsync(Hwnd, null, new ElementLocator(Name: "Apply"), new ElementAction.Invoke(), Token));
        await AssertElevated(() => interaction.SendKeysAsync(Hwnd, null, new ElementLocator(), new KeyInput.Text("x"), Token));
        await AssertElevated(() => screenshots.CaptureAsync(Hwnd, null, new ElementLocator(), 0, 1280, Token));
    }

    [Fact]
    public void Window_facts_of_an_elevated_application_stay_available()
    {
        var (windows, _, _, _) = Services();

        Assert.Equal(true, Assert.Single(windows.List(new WindowFilter()).Windows).Process.Elevated);
        Assert.Equal("Admin Tool", windows.Inspect(Hwnd).Window.Title);
    }

    [Fact]
    public async Task An_elevated_WinMCP_may_operate_elevated_applications()
    {
        _desktop.CurrentProcessElevated = true;
        var (_, tree, _, _) = Services();

        Assert.Equal("Admin Tool", (await tree.GetTreeAsync(Hwnd, null, 10, 100, Token)).Root.Name);
    }
}
