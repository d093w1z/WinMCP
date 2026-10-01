using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;
using WinMcp.Core.Native;
using WinMcp.Core.Policy;
using WinMcp.Core.Symbols;
using WinMcp.Testing;
using static WinMcp.Testing.FakeUiAutomation;

namespace WinMcp.Core.Tests.Automation;

/// <summary>M11: what an MFC process gets without any configuration — standard symbols and wrapper-class guesses.</summary>
public sealed class MfcTreeTests
{
    private const string Exe = @"C:\Apps\Frame\Frame.exe";
    private static readonly WindowInfo Frame = FakeDesktop.Window("Frame", "Frame App", pid: 3, hwnd: 0x30, className: "AfxFrameOrView140u", path: Exe);
    private static readonly WindowInfo Plain = FakeDesktop.Window("Plain", "Plain", pid: 4, hwnd: 0x40, className: "#32770");

    private readonly FakeUiAutomation _automation = new();
    private readonly UiTreeService _service;

    public MfcTreeTests()
    {
        _automation.Trees[Frame.Hwnd] = (Element("3", "Window", "Frame App", className: "AfxFrameOrView140u") with { NativeWindowHandle = 0x30 }).With(
            Element("3.1", "Pane", "", "59648", className: "AfxFrameOrView140u") with { NativeWindowHandle = 0x31 },
            Element("3.2", "ToolBar", "", "59392", className: "ToolbarWindow32") with { NativeWindowHandle = 0x32 },
            Element("3.3", "List", "PropertyList", "1306", className: "Afx:PropList:1:8:10003:10") with { NativeWindowHandle = 0x33 },
            (Element("3.4", "Header", "Header Control", "Header", className: "SysHeader32") with { NativeWindowHandle = 0x34 }),
            (Element("3.6", "Pane", "", "59443", className: "BCGPControlBar") with { NativeWindowHandle = 0x36 }).With(
                Element("3.6.1", "Button", "Pin", "1", className: "Button") with { NativeWindowHandle = 0x37 }),
            (Element("3.7", "Pane", "", "59649", className: "#32770") with { NativeWindowHandle = 0x38 }).With(
                Element("3.7.1", "Button", "OK", "1", className: "Button") with { NativeWindowHandle = 0x39 }),
            Element("3.5", "MenuBar", "Application").With(
                Element("3.5.1", "MenuItem", "Exit", "57665")));
        _automation.Extras["3.1"] = new ElementExtras("Win32", true, "", null, [], 0xE900, null);
        _automation.Extras["3.2"] = new ElementExtras("Win32", false, "", null, [], 0xE800, null);
        _automation.Extras["3.3"] = new ElementExtras("Win32", false, "", null, [], 1306, null);
        _automation.Extras["3.4"] = new ElementExtras("Win32", false, "", null, [], 1, null); // an ID inside the grid, not IDOK
        _automation.Trees[Plain.Hwnd] = (Element("4", "Window", "Plain", className: "#32770") with { NativeWindowHandle = 0x40 }).With(
            Element("4.1", "Button", "OK", "1", className: "Button") with { NativeWindowHandle = 0x41 });
        _automation.Extras["4.1"] = new ElementExtras("Win32", true, "", null, ["Invoke"], 1, null);

        var native = new FakeNativeProcesses();
        native.Modules[3] = [FakeNativeProcesses.Module(Exe), FakeNativeProcesses.Module(@"C:\Windows\System32\mfc140u.dll")];
        native.Modules[4] = [FakeNativeProcesses.Module(@"C:\Apps\Plain.exe")];
        var options = WinMcpOptions.Parse(["--allow", "Frame", "--allow", "Plain"]);
        var symbols = new SymbolProvider(options);
        var nativeInfo = new NativeAppInfo(native, symbols);
        var desktop = new FakeDesktop(Frame, Plain);
        _service = new UiTreeService(new WindowQuery(desktop, new TargetPolicy(options, 999), nativeInfo), _automation, new ElementRegistry(), symbols, nativeInfo);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Frame_ids_and_commands_get_MFC_symbols_without_a_resource_h()
    {
        var outline = OutlineRenderer.Render(await _service.GetTreeAsync("hwnd:0x00000030", null, 10, 100, Token));

        Assert.Contains("Pane \"\" #59648 (AFX_IDW_PANE_FIRST)", outline);
        Assert.Contains("ToolBar \"\" #59392 (AFX_IDW_TOOLBAR)", outline);
        Assert.Contains("MenuItem \"Exit\" #57665 (ID_APP_EXIT)", outline); // a command, not ID_VIEW_*/AFX_IDW_*
    }

    [Fact]
    public async Task Standard_dialog_ids_only_name_controls_of_dialogs()
    {
        var lines = OutlineRenderer.Render(await _service.GetTreeAsync("hwnd:0x00000030", null, 10, 100, Token)).Split('\n');

        Assert.Contains(lines, l => l.Contains("Button \"Pin\" #1") && !l.Contains("IDOK")); // a BCG pane's child 1 (real-world app, M11)
        Assert.Contains(lines, l => l.Contains("Button \"OK\" #1 (IDOK)")); // inside a (form view) dialog it is IDOK
    }

    [Fact]
    public async Task Command_symbols_work_as_locators()
    {
        var matches = await _service.FindAsync("hwnd:0x00000030", null, new ElementLocator(ControlSymbol: "ID_APP_EXIT"), 5, Token);

        Assert.Equal("Exit", Assert.Single(matches.Matches).Name);
    }

    [Fact]
    public async Task Inspect_guesses_the_MFC_class_for_window_backed_elements()
    {
        async Task<ElementDetail> Inspect(string automationId) =>
            await _service.InspectAsync("hwnd:0x00000030", null, new ElementLocator(AutomationId: automationId), Token);

        Assert.Equal("CView", (await Inspect("59648")).MfcClassGuess);
        Assert.Equal("CToolBar", (await Inspect("59392")).MfcClassGuess);
        Assert.Equal("CMFCPropertyGridCtrl", (await Inspect("1306")).MfcClassGuess);
        Assert.Null((await Inspect("57665")).MfcClassGuess); // windowless
        Assert.Equal(new SuggestedLocator(null, "AFX_IDW_TOOLBAR", null, null, Unique: true), (await Inspect("59392")).Locator);
    }

    [Fact]
    public async Task Control_ids_inside_other_controls_get_no_symbol()
    {
        var header = await _service.InspectAsync("hwnd:0x00000030", null, new ElementLocator(ControlType: "Header"), Token);

        Assert.Equal((1, null), (header.ControlId, header.ControlSymbol));
    }

    [Fact]
    public async Task Non_MFC_processes_get_neither_guesses_nor_MFC_symbols()
    {
        var ok = await _service.InspectAsync("hwnd:0x00000040", null, new ElementLocator(AutomationId: "1"), Token);

        Assert.Equal((null, null), (ok.ControlSymbol, ok.MfcClassGuess)); // no table at all: no resource.h, not MFC
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => _service.FindAsync("hwnd:0x00000040", null, new ElementLocator(ControlSymbol: "ID_APP_EXIT"), 5, Token));
        Assert.Contains("needs a resource.h", ex.Error.Message);
    }
}
