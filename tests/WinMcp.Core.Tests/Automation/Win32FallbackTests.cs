using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;
using WinMcp.Core.Policy;
using WinMcp.Core.Symbols;
using WinMcp.Testing;
using static WinMcp.Testing.FakeDesktop;

namespace WinMcp.Core.Tests.Automation;

public sealed class Win32TreeBuilderTests
{
    private static readonly WindowInfo Dialog = Window("App", "Confirm", pid: 1, hwnd: 0x10, className: "#32770");

    [Fact]
    public void Maps_classes_and_styles_to_control_types_and_names_inputs_after_labels()
    {
        var tree = Win32TreeBuilder.Build(Dialog,
        [
            Child(0x11, 0x10, "Static", "Note:", controlId: 100),
            Child(0x12, 0x10, "Edit", "hello", controlId: 101),
            Child(0x13, 0x10, "Button", "Remember me", controlId: 102, style: 0x3, check: 1), // BS_AUTOCHECKBOX
            Child(0x14, 0x10, "WindowsForms10.BUTTON.app.0.1", "OK", controlId: 1),            // BS_PUSHBUTTON
            Child(0x15, 0x10, "Edit", "", controlId: 103, style: 0x20),                        // ES_PASSWORD
        ]);

        Assert.Equal(Win32TreeBuilder.RuntimeIdPrefix + "hwnd:0x00000010", tree.RuntimeId);
        var c = tree.Children;
        Assert.Equal(("Text", "Note:"), (c[0].ControlType, c[0].Name));
        Assert.Equal(("Edit", "Note:", "hello", "101"), (c[1].ControlType, c[1].Name, c[1].Value, c[1].AutomationId));
        Assert.Equal(("CheckBox", "on"), (c[2].ControlType, c[2].ToggleState));
        Assert.Equal(("Button", "OK", "1"), (c[3].ControlType, c[3].Name, c[3].AutomationId));
        Assert.True(c[4].IsPassword);
        Assert.Null(c[4].Value);
    }

    [Fact]
    public void Nests_children_under_their_parent_windows()
    {
        var tree = Win32TreeBuilder.Build(Dialog, [Child(0x20, 0x10, "#32770", ""), Child(0x21, 0x20, "Button", "Inner")]);

        Assert.Equal("Inner", Assert.Single(Assert.Single(tree.Children).Children).Name);
    }

    [Fact]
    public void Fallback_ids_round_trip_to_handles()
    {
        var id = Win32TreeBuilder.RuntimeIdOf(new WindowHandle(0xABC));

        Assert.True(Win32TreeBuilder.IsFallbackId(id));
        Assert.Equal(new WindowHandle(0xABC), Win32TreeBuilder.HandleOf(id));
        Assert.False(Win32TreeBuilder.IsFallbackId("42.1.2"));
    }
}

public sealed class Win32FallbackTests
{
    private static readonly WindowInfo Box = Window("App", "About", pid: 1, hwnd: 0x10, className: "#32770");

    [Fact]
    public async Task Uia_timeout_on_a_responding_window_falls_back_to_child_windows()
    {
        var (service, _) = Create(responding: true);

        var tree = await service.GetTreeAsync("hwnd:0x00000010", null, 10, 300, TestContext.Current.CancellationToken);

        Assert.Equal(UiTreeService.Win32Source, tree.Source);
        Assert.Equal("OK", Assert.Single(tree.Root.Children!, n => n.ControlType == "Button").Name);
        Assert.StartsWith("(UI Automation is not answering", OutlineRenderer.Render(tree));
    }

    [Fact]
    public async Task Find_reports_the_fallback_source_too()
    {
        var (service, _) = Create(responding: true);

        var found = await service.FindAsync("hwnd:0x00000010", null, new ElementLocator(Name: "OK"), 5, TestContext.Current.CancellationToken);

        Assert.Equal(UiTreeService.Win32Source, found.Source);
        Assert.Single(found.Matches);
    }

    [Fact]
    public async Task Window_that_stopped_answering_messages_is_reported_as_hung_before_Windows_flags_it()
    {
        var (service, desktop) = Create(responding: true);        // Windows' hung flag not set yet...
        desktop.NotAnsweringMessages.Add(Box.Hwnd);                // ...but the thread isn't pumping

        var ex = await Assert.ThrowsAsync<WinMcpException>(() => service.GetTreeAsync("hwnd:0x00000010", null, 10, 300, TestContext.Current.CancellationToken));

        Assert.Equal(WinMcpErrorCode.TargetNotResponding, ex.Error.Code);
    }

    [Fact]
    public async Task Hung_window_does_not_fall_back()
    {
        var (service, _) = Create(responding: false);

        var ex = await Assert.ThrowsAsync<WinMcpException>(() => service.GetTreeAsync("hwnd:0x00000010", null, 10, 300, TestContext.Current.CancellationToken));

        Assert.Equal(WinMcpErrorCode.TargetNotResponding, ex.Error.Code);
    }

    private static (UiTreeService Service, FakeDesktop Desktop) Create(bool responding)
    {
        var desktop = new FakeDesktop(Box);
        desktop.Details[Box.Hwnd] = DefaultDetails(Box) with { Responding = responding };
        desktop.Children[Box.Hwnd] = [Child(0x11, 0x10, "Static", "WinMCP Test App v2"), Child(0x12, 0x10, "Button", "OK", controlId: 2)];
        var automation = new FakeUiAutomation { ThrowOnFetch = new WinMcpException(new WinMcpError(WinMcpErrorCode.TargetNotResponding, "uia timed out")) };
        var options = new WinMcpOptions(ServerMode.Control, ["App"]);
        var windows = new WindowQuery(desktop, new TargetPolicy(options, 999));
        return (new UiTreeService(windows, automation, new ElementRegistry(), new SymbolProvider(options)), desktop);
    }
}
