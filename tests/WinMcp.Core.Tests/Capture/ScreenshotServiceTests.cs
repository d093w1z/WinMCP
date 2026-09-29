using WinMcp.Core.Automation;
using WinMcp.Core.Capture;
using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;
using WinMcp.Core.Policy;
using WinMcp.Core.Symbols;
using WinMcp.Testing;
using static WinMcp.Testing.FakeUiAutomation;

namespace WinMcp.Core.Tests.Capture;

public sealed class ScreenshotServiceTests
{
    private const string Hwnd = "hwnd:0x00000010";
    private static readonly WindowInfo Main = FakeDesktop.Window("WinMcp.TestApp", "WinMCP Test App", pid: 1, hwnd: 0x10); // bounds 10,20 300x200
    private static readonly WindowInfo Mail = FakeDesktop.Window("mail", "Inbox", pid: 2, hwnd: 0x20);

    private readonly FakeDesktop _desktop = new(Main, Mail);
    private readonly FakeUiAutomation _automation = new();
    private readonly FakeScreenCapture _capture = new();

    public ScreenshotServiceTests()
    {
        _automation.Trees[Main.Hwnd] = Element("1", "Window", "WinMCP Test App").With(
            Element("1.1", "Button", "Apply", "applyButton") with { Bounds = new Rect(100, 150, 80, 25) },
            Element("1.2", "Button", "Edge", "edgeButton") with { Bounds = new Rect(290, 200, 40, 40) },  // partly outside the window
            Element("1.3", "Button", "Scrolled", "scrolledButton", offscreen: true) with { Bounds = new Rect(100, 150, 80, 25) },
            Element("1.4", "Button", "Collapsed", "zeroButton") with { Bounds = new Rect(0, 0, 0, 0) });
    }

    private ScreenshotService Service()
    {
        var options = new WinMcpOptions(ServerMode.Observe, ["WinMcp.TestApp"]);
        var windows = new WindowQuery(_desktop, new TargetPolicy(options, 999));
        return new ScreenshotService(windows, new UiTreeService(windows, _automation, new ElementRegistry(), new SymbolProvider(options)), _capture);
    }

    private Task<Screenshot> Capture(string? hwnd = Hwnd, ElementLocator? locator = null, int padding = 0, int maxEdge = 1280) =>
        Service().CaptureAsync(hwnd, null, locator ?? new ElementLocator(), padding, maxEdge, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Window_capture_uses_the_visible_window_bounds()
    {
        var shot = await Capture();

        Assert.Equal((Main.Hwnd, Main.Bounds, 1280), Assert.Single(_capture.Requests));
        Assert.Equal(FakeScreenCapture.Png, shot.Png);
        Assert.Equal(Main.Hwnd, shot.Info.Window);
        Assert.Null(shot.Info.Element);
        Assert.Equal(96, shot.Info.Dpi);
    }

    [Fact]
    public async Task Element_capture_adds_padding()
    {
        var shot = await Capture(locator: new ElementLocator(AutomationId: "applyButton"), padding: 5);

        Assert.Equal(new Rect(95, 145, 90, 35), Assert.Single(_capture.Requests).Region);
        Assert.NotNull(shot.Info.Element);
    }

    [Fact]
    public async Task Element_capture_is_clipped_to_the_window()
    {
        await Capture(locator: new ElementLocator(AutomationId: "edgeButton"));

        Assert.Equal(new Rect(290, 200, 20, 20), Assert.Single(_capture.Requests).Region); // window ends at 310,220
    }

    [Theory]
    [InlineData("scrolledButton")]
    [InlineData("zeroButton")]
    public async Task Invisible_elements_are_offscreen_errors(string automationId)
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Capture(locator: new ElementLocator(AutomationId: automationId)));

        Assert.Equal(WinMcpErrorCode.ElementOffscreen, ex.Error.Code);
        Assert.Empty(_capture.Requests);
    }

    [Fact]
    public async Task Minimized_window_is_reported_not_captured()
    {
        _desktop.Windows[0] = Main with { Minimized = true };

        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Capture());

        Assert.Equal(WinMcpErrorCode.WindowMinimized, ex.Error.Code);
        Assert.Empty(_capture.Requests);
    }

    [Fact]
    public async Task Non_allowlisted_window_is_never_captured()
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Capture(hwnd: "hwnd:0x00000020"));

        Assert.Equal(WinMcpErrorCode.WindowNotFound, ex.Error.Code);
        Assert.Empty(_capture.Requests);
    }

    [Theory]
    [InlineData(0, 63)]
    [InlineData(0, 4097)]
    [InlineData(-1, 1280)]
    [InlineData(201, 1280)]
    public async Task Limits_are_validated(int padding, int maxEdge) =>
        Assert.Equal(WinMcpErrorCode.InvalidArgument,
            (await Assert.ThrowsAsync<WinMcpException>(() => Capture(padding: padding, maxEdge: maxEdge))).Error.Code);
}
