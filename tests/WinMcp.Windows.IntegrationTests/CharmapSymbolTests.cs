using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;
using FlaApplication = FlaUI.Core.Application;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>Character Map: a classic Win32 #32770 dialog, standing in for MFC until the MFC test app exists (M10).</summary>
public sealed class CharmapSession : IDisposable
{
    private readonly FlaApplication _app;
    private readonly UIA3Automation _automation = new();

    public CharmapSession()
    {
        _app = FlaApplication.Launch("charmap.exe");
        WinMcp.Testing.ChildProcessJob.Add(_app.ProcessId);
        Window = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(15)) ?? throw new InvalidOperationException("charmap did not start");
        HeaderPath = Path.Combine(Path.GetTempPath(), $"winmcp-charmap-{Guid.NewGuid():N}-resource.h");
        // Control IDs observed in M0. IDD_CHARMAP deliberately shares 104 with the edit; IDC_COPY has an alias.
        File.WriteAllText(HeaderPath, """
            #define IDD_CHARMAP        104
            #define IDC_CHARGRID       108
            #define IDC_CHARS_TO_COPY  104
            #define IDC_SELECT         103
            #define IDC_COPY           102
            #define IDC_COPY_ALIAS     102
            #define IDC_FONT           105
            #define IDC_ADVANCED_VIEW  119
            """);
    }

    public Window Window { get; }

    public string HeaderPath { get; }

    public WindowHandle Handle => new(Window.Properties.NativeWindowHandle.Value);

    public void Dispose()
    {
        _app.Kill();
        _app.Dispose();
        _automation.Dispose();
        File.Delete(HeaderPath);
    }
}

[Trait("Category", "Windows")]
public sealed class CharmapSymbolTests : IClassFixture<CharmapSession>, IDisposable
{
    private readonly CharmapSession _charmap;
    private readonly WinMcpServices _services;

    public CharmapSymbolTests(CharmapSession charmap)
    {
        _charmap = charmap;
        _services = new WinMcpServices("--allow", "charmap", "--symbols", $"charmap={charmap.HeaderPath}");
    }

    public void Dispose() => _services.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private Task<ElementDetail> Inspect(ElementLocator locator) =>
        _services.Tree.InspectAsync(_charmap.Handle.ToString(), null, locator, Token);

    [Fact]
    public async Task Outline_shows_resource_h_names_next_to_control_ids()
    {
        var outline = OutlineRenderer.Render(await _services.Tree.GetTreeAsync(_charmap.Handle.ToString(), null, 10, 300, Token));

        Assert.Contains("#104 (IDC_CHARS_TO_COPY)", outline); // not IDD_CHARMAP
        Assert.Contains("#103 (IDC_SELECT)", outline);
        Assert.Contains("#105 (IDC_FONT)", outline);
        Assert.DoesNotContain("IDC_COPY", outline); // ambiguous: shown without a name
    }

    [Fact]
    public async Task Control_symbol_finds_the_real_control_and_matches_GetDlgCtrlID()
    {
        var detail = await Inspect(new ElementLocator(ControlSymbol: "IDC_SELECT"));

        Assert.Equal(("Button", "Select"), (detail.ControlType, detail.Name));
        Assert.Equal(103, detail.ControlId);
        Assert.Equal("IDC_SELECT", detail.ControlSymbol);
        Assert.NotNull(detail.Hwnd);
        Assert.Equal("Win32", detail.FrameworkId);
        Assert.Equal(new SuggestedLocator("103", null, null, null, Unique: true), detail.Locator);
    }

    [Fact]
    public async Task Shared_id_reports_candidates()
    {
        var detail = await Inspect(new ElementLocator(AutomationId: "102"));

        Assert.Null(detail.ControlSymbol);
        Assert.Equal(["IDC_COPY", "IDC_COPY_ALIAS"], detail.ControlSymbolCandidates);
    }

    [Fact]
    public async Task Font_combo_options_are_readable_from_a_native_combo()
    {
        var detail = await Inspect(new ElementLocator(ControlSymbol: "IDC_FONT"));

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"charmap font combo: {detail.Options?.Count.ToString() ?? "null"} of {detail.OptionCount} options; first: {detail.Options?.FirstOrDefault()}");
        Assert.Equal("ComboBox", detail.ControlType);
        Assert.NotNull(detail.Options);
        Assert.NotEmpty(detail.Options);
        // Installed fonts usually exceed the cap; option_count tells the agent the list is incomplete.
        Assert.Equal(Math.Min(detail.OptionCount!.Value, ElementExtras.MaxOptions), detail.Options.Count);
    }
}
