using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;
using WinMcp.Core.Policy;
using WinMcp.Testing;

namespace WinMcp.Core.Tests.Desktop;

public sealed class WindowQueryInspectTests
{
    private static readonly WindowInfo Main = FakeDesktop.Window("WinMcp.TestApp", "WinMCP Test App", pid: 1, hwnd: 0x10, className: "WindowsForms10.Window.8.app.0.1");
    private static readonly WindowInfo Dialog = FakeDesktop.Window("WinMcp.TestApp", "Options", pid: 1, hwnd: 0x11, owner: 0x10);
    private static readonly WindowInfo HiddenOwned = FakeDesktop.Window("WinMcp.TestApp", "Tooltip", pid: 1, hwnd: 0x12, owner: 0x10, visible: false);
    private static readonly WindowInfo ForeignOwned = FakeDesktop.Window("picker", "Choose file", pid: 3, hwnd: 0x13, owner: 0x10);
    private static readonly WindowInfo Mail = FakeDesktop.Window("mail", "Password reset for bob", pid: 2, hwnd: 0x20);

    private static (WindowQuery Query, FakeDesktop Desktop) Create()
    {
        var desktop = new FakeDesktop(Main, Dialog, HiddenOwned, ForeignOwned, Mail);
        var policy = new TargetPolicy(new WinMcpOptions(ServerMode.Observe, ["WinMcp.TestApp"]), ownPid: 999);
        return (new WindowQuery(desktop, policy), desktop);
    }

    [Fact]
    public void Inspects_an_allowlisted_top_level_window()
    {
        var (query, desktop) = Create();
        desktop.Details[Main.Hwnd] = FakeDesktop.DefaultDetails(Main, "WindowsForms10.BUTTON.app.0.1", "WindowsForms10.EDIT.app.0.1", "WindowsForms10.BUTTON.app.0.1");

        var result = query.Inspect("hwnd:0x00000010");

        Assert.Same(Main, result.Window);
        Assert.True(result.Responding);
        Assert.Contains("WS_CAPTION", result.Styles);
        Assert.Contains("WS_EX_APPWINDOW", result.ExtendedStyles);
        Assert.Equal("winforms", result.FrameworkHint);
        Assert.Equal(3, result.ChildWindows.Count);
        Assert.Equal(2, result.ChildWindows.ByClass["WindowsForms10.BUTTON.app.0.1"]);
        Assert.Equal("WindowsForms10.BUTTON.app.0.1", result.ChildWindows.ByClass.Keys.First()); // most frequent first
    }

    [Fact]
    public void Owned_windows_include_only_shown_allowlisted_windows()
    {
        var (query, _) = Create();

        var owned = query.Inspect("hwnd:0x00000010").OwnedWindows;

        var dialog = Assert.Single(owned);
        Assert.Equal(new WindowSummary(Dialog.Hwnd, "Options", Dialog.ClassName), dialog);
    }

    [Fact]
    public void Child_class_summary_is_capped()
    {
        var (query, desktop) = Create();
        var classes = Enumerable.Range(0, WindowQuery.MaxChildClassesReported + 5).Select(i => $"Class{i}").ToArray();
        desktop.Details[Main.Hwnd] = FakeDesktop.DefaultDetails(Main, classes);

        var children = query.Inspect("hwnd:0x00000010").ChildWindows;

        Assert.Equal(classes.Length, children.Count);
        Assert.Equal(WindowQuery.MaxChildClassesReported, children.ByClass.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0x10")]
    [InlineData("hwnd:0xnothex")]
    public void Malformed_handle_is_an_invalid_argument(string? hwnd)
    {
        var ex = Assert.Throws<WinMcpException>(() => Create().Query.Inspect(hwnd));

        Assert.Equal(WinMcpErrorCode.InvalidArgument, ex.Error.Code);
    }

    [Fact]
    public void Non_allowlisted_window_is_indistinguishable_from_a_missing_one()
    {
        var (query, _) = Create();

        var foreign = Assert.Throws<WinMcpException>(() => query.Inspect("hwnd:0x00000020"));
        var missing = Assert.Throws<WinMcpException>(() => query.Inspect("hwnd:0x00000099"));

        Assert.Equal(WinMcpErrorCode.WindowNotFound, foreign.Error.Code);
        Assert.Equal(missing.Error with { Message = "" }, foreign.Error with { Message = "" });
        Assert.DoesNotContain("mail", foreign.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Child_window_is_rejected_with_guidance()
    {
        var (query, desktop) = Create();
        var child = FakeDesktop.Window("WinMcp.TestApp", "", pid: 1, hwnd: 0x30);
        desktop.Details[child.Hwnd] = FakeDesktop.DefaultDetails(child) with { Parent = Main.Hwnd };

        var ex = Assert.Throws<WinMcpException>(() => query.Inspect("hwnd:0x00000030"));

        Assert.Equal(WinMcpErrorCode.InvalidArgument, ex.Error.Code);
        Assert.Contains("child window", ex.Error.Message);
    }
}
