using System.Text.Json;
using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;
using WinMcp.Core.Policy;
using WinMcp.Core.Symbols;
using WinMcp.Testing;
using static WinMcp.Testing.FakeUiAutomation;

namespace WinMcp.Core.Tests.Automation;

public sealed class InspectElementTests : IDisposable
{
    private static readonly WindowInfo TestApp = FakeDesktop.Window("WinMcp.TestApp", "WinMCP Test App", pid: 1, hwnd: 0x10);
    private static readonly WindowInfo Dialog = FakeDesktop.Window("Legacy", "Settings", pid: 2, hwnd: 0x20, className: "#32770");

    private readonly string _header = Path.Combine(Path.GetTempPath(), $"winmcp-{Guid.NewGuid():N}-resource.h");
    private readonly FakeUiAutomation _automation = new();
    private readonly UiTreeService _service;

    public InspectElementTests()
    {
        File.WriteAllText(_header, """
            #define IDD_SETTINGS    1000
            #define IDC_EDIT_NAME   1000
            #define IDC_COMBO_TYPE  1001
            #define IDC_OLD_APPLY   1002
            #define IDC_APPLY       1002
            """);

        _automation.Trees[TestApp.Hwnd] = TestAppTree() with { NativeWindowHandle = 0x10 };
        // Win32 dialog: AutomationId == control ID (M0 finding 3); elements with their own HWND.
        _automation.Trees[Dialog.Hwnd] = (Element("2", "Window", "Settings", className: "#32770") with { NativeWindowHandle = 0x20 }).With(
            Element("2.1", "Text", "Name:", "1100"),
            Element("2.2", "Edit", "Name:", "1000", value: "Mukesh") with { NativeWindowHandle = 0x21 },
            Element("2.3", "ComboBox", "Type:", "1001", value: "HTML", expand: "collapsed") with { NativeWindowHandle = 0x22 },
            Element("2.4", "Button", "Apply", "1002") with { NativeWindowHandle = 0x23 },
            Element("2.5", "Button", "OK", "1") with { NativeWindowHandle = 0x24 },
            Element("2.6", "List", "Items", "1200").With(Element("2.6.1", "ListItem", "First")));
        _automation.Extras["2.2"] = new ElementExtras("Win32", true, "Your full name", "2.1", ["Text", "Value"], 1000, null);
        _automation.Extras["2.3"] = new ElementExtras("Win32", true, "", null, ["ExpandCollapse", "Value"], 1001, ["Text", "HTML", "Markdown"]);
        _automation.Extras["2.4"] = new ElementExtras("Win32", true, "", null, ["Invoke"], 1002, null);

        var options = WinMcpOptions.Parse(["--allow", "WinMcp.TestApp", "--allow", "Legacy", "--symbols", $"Legacy={_header}"]);
        var desktop = new FakeDesktop(TestApp, Dialog);
        _service = new UiTreeService(new WindowQuery(desktop, new TargetPolicy(options, 999)), _automation, new ElementRegistry(), new SymbolProvider(options));
    }

    public void Dispose() => File.Delete(_header);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private Task<ElementDetail> Inspect(string? hwnd = null, string? element = null, ElementLocator? locator = null) =>
        _service.InspectAsync(hwnd, element, locator ?? new ElementLocator(), Token);

    [Fact]
    public async Task Inspects_a_win32_control_with_hwnd_control_id_symbol_label_and_help()
    {
        var detail = await Inspect("hwnd:0x00000020", locator: new ElementLocator(AutomationId: "1000"));

        Assert.Equal(("Edit", "Name:", "Mukesh"), (detail.ControlType, detail.Name, detail.Value));
        Assert.Equal(new WindowHandle(0x21), detail.Hwnd);
        Assert.Equal(new WindowHandle(0x21), detail.HostHwnd);
        Assert.Equal(1000, detail.ControlId);
        Assert.Equal("IDC_EDIT_NAME", detail.ControlSymbol); // not IDD_SETTINGS, which shares the number
        Assert.Equal("Win32", detail.FrameworkId);
        Assert.Equal(["Text", "Value"], detail.Patterns);
        Assert.Equal("Your full name", detail.HelpText);
        Assert.NotNull(detail.LabeledBy);
        Assert.NotNull(detail.Parent);
    }

    [Fact]
    public async Task Combo_options_come_from_the_automation_layer()
    {
        var detail = await Inspect("hwnd:0x00000020", locator: new ElementLocator(ControlSymbol: "IDC_COMBO_TYPE"));

        Assert.Equal(["Text", "HTML", "Markdown"], detail.Options);
        Assert.Contains("collapsed", detail.States!);
    }

    [Fact]
    public async Task Shared_control_id_lists_candidates_instead_of_choosing()
    {
        var detail = await Inspect("hwnd:0x00000020", locator: new ElementLocator(AutomationId: "1002"));

        Assert.Null(detail.ControlSymbol);
        Assert.Equal(["IDC_APPLY", "IDC_OLD_APPLY"], detail.ControlSymbolCandidates);
    }

    [Fact]
    public async Task Windowless_element_reports_its_host_window()
    {
        var detail = await Inspect("hwnd:0x00000020", locator: new ElementLocator(Name: "First"));

        Assert.Null(detail.Hwnd);
        Assert.Equal(new WindowHandle(0x20), detail.HostHwnd);
    }

    [Fact]
    public async Task Ref_alone_identifies_the_element_and_matches_tree_refs()
    {
        var tree = await _service.GetTreeAsync("hwnd:0x00000010", null, 10, 300, Token);
        var apply = tree.Root.Children!.Single(n => n.AutomationId == "applyButton");

        var detail = await Inspect(element: apply.Ref);

        Assert.Equal(apply.Ref, detail.Ref);
        Assert.Equal(tree.Root.Ref, detail.Parent);
    }

    [Fact]
    public async Task Suggested_locator_prefers_a_unique_automation_id()
    {
        var detail = await Inspect("hwnd:0x00000010", locator: new ElementLocator(AutomationId: "applyButton"));

        Assert.Equal(new SuggestedLocator("applyButton", null, null, null, Unique: true), detail.Locator);
    }

    [Fact]
    public async Task Suggested_locator_falls_back_to_name_and_type_when_there_is_no_automation_id()
    {
        var detail = await Inspect("hwnd:0x00000020", locator: new ElementLocator(Name: "First"));

        Assert.Equal(new SuggestedLocator(null, null, "First", "ListItem", Unique: true), detail.Locator);
    }

    [Fact]
    public async Task Ambiguous_locator_lists_candidate_refs()
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Inspect("hwnd:0x00000010", locator: new ElementLocator(ControlType: "Button")));

        Assert.Equal(WinMcpErrorCode.AmbiguousMatch, ex.Error.Code);
        var candidates = JsonSerializer.SerializeToElement(ex.Error.Details).GetProperty("candidates");
        Assert.Equal(3, candidates.GetArrayLength());
        Assert.StartsWith("e", candidates[0].GetProperty("ref").GetString());
    }

    [Fact]
    public async Task No_match_is_element_not_found()
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Inspect("hwnd:0x00000010", locator: new ElementLocator(AutomationId: "nope")));

        Assert.Equal(WinMcpErrorCode.ElementNotFound, ex.Error.Code);
    }

    [Fact]
    public async Task Neither_ref_nor_criteria_is_invalid()
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => Inspect("hwnd:0x00000010"));

        Assert.Equal(WinMcpErrorCode.InvalidArgument, ex.Error.Code);
    }

    [Fact]
    public async Task Password_field_never_exposes_options_or_value()
    {
        _automation.Trees[TestApp.Hwnd] = Element("1", "Window", "App").With(Element("1.1", "Edit", "PIN", "pin", value: "1234", password: true));
        _automation.Extras["1.1"] = new ElementExtras("WinForm", true, "", null, ["Value"], null, ["1234"]);

        var detail = await Inspect("hwnd:0x00000010", locator: new ElementLocator(AutomationId: "pin"));

        Assert.Null(detail.Value);
        Assert.Null(detail.Options);
    }

    [Fact]
    public async Task Tree_and_find_use_control_symbols()
    {
        var tree = await _service.GetTreeAsync("hwnd:0x00000020", null, 10, 300, Token);
        var outline = OutlineRenderer.Render(tree);

        Assert.Contains("Edit \"Name:\" #1000 (IDC_EDIT_NAME)", outline);
        Assert.Contains("Button \"OK\" #1 (IDOK)", outline);
        Assert.DoesNotContain("IDC_OLD_APPLY", outline); // ambiguous: no symbol shown

        var found = await _service.FindAsync("hwnd:0x00000020", null, new ElementLocator(ControlSymbol: "idc_edit_name"), 25, Token);
        Assert.Equal("Name:", Assert.Single(found.Matches).Name);
    }

    [Fact]
    public async Task Control_symbol_without_configured_resource_h_explains_how_to_enable_it()
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() =>
            _service.FindAsync("hwnd:0x00000010", null, new ElementLocator(ControlSymbol: "IDC_EDIT_NAME"), 25, Token));

        Assert.Equal(WinMcpErrorCode.InvalidArgument, ex.Error.Code);
        Assert.Contains("--symbols WinMcp.TestApp=", ex.Error.Hint);
    }

    [Fact]
    public async Task Unknown_control_symbol_is_invalid()
    {
        var ex = await Assert.ThrowsAsync<WinMcpException>(() =>
            _service.FindAsync("hwnd:0x00000020", null, new ElementLocator(ControlSymbol: "IDC_NOPE"), 25, Token));

        Assert.Equal(WinMcpErrorCode.InvalidArgument, ex.Error.Code);
    }
}
