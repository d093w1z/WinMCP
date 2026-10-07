using System.Diagnostics;
using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;
using WinMcp.Core.Native;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>M11 on the MFC dialog app: framework detection, dialog templates, wrapper-class guesses, element identity.</summary>
[Trait("Category", "Windows")]
public sealed class MfcNativeInfoTests(MfcAppUnderTest app) : IClassFixture<MfcAppUnderTest>, IDisposable
{
    private readonly WinMcpServices _services = new(app.SkipReason is null
        ? app.ControlModeArguments(Path.Combine(Path.GetTempPath(), $"winmcp-audit-{Guid.NewGuid():N}"))
        : ["--allow", "WinMcp.MfcTestApp"]);

    public void Dispose() => _services.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Hwnd => app.Handle.ToString();

    private void RequireApp() => Assert.SkipWhen(app.SkipReason is not null, app.SkipReason ?? "");

    private Task<ActionResult> Act(ElementLocator locator, ElementAction action, string? hwnd = null) =>
        _services.Interaction.PerformAsync(hwnd ?? Hwnd, null, locator, action, Token);

    [Fact]
    public void Inspect_window_reports_shared_MFC_and_the_dialog_template()
    {
        RequireApp();

        var inspection = _services.Windows.Inspect(Hwnd);

        Assert.Equal(("mfc", "shared"), (inspection.Framework!.Name, inspection.Framework.Linkage));
        Assert.StartsWith("14.", inspection.Framework.Version);
        Assert.StartsWith("loaded module mfc140u.dll", inspection.Framework.Evidence[0]);
        var dialog = Assert.Single(inspection.DialogResources!);
        Assert.Equal((100, "IDD_MAIN", "WinMcp.MfcTestApp.exe", 1.0), (dialog.ResourceId, dialog.Symbol, dialog.Module, dialog.Match));
        Assert.Equal([new ControlRef(1015, "IDC_EDIT_HIDDEN")], dialog.HiddenControls);
        Assert.Empty(dialog.ExtraControls);
    }

    [Fact]
    public async Task Controls_created_at_run_time_show_up_as_extra()
    {
        RequireApp();
        try
        {
            await Act(new ElementLocator(ControlSymbol: "IDC_BUTTON_ADD_FIELD"), new ElementAction.Invoke());

            var dialog = Assert.Single(_services.Windows.Inspect(Hwnd).DialogResources!);

            Assert.Equal("IDD_MAIN", dialog.Symbol);
            Assert.Equal(["IDC_EDIT_DYNAMIC1", "IDC_STATIC_DYNAMIC1"], dialog.ExtraControls.Select(c => c.Symbol).Order());
        }
        finally
        {
            await Act(new ElementLocator(ControlSymbol: "IDC_BUTTON_CANCEL"), new ElementAction.Invoke()); // Reset
        }
    }

    [Fact]
    public async Task Modal_dialog_is_matched_to_its_own_template()
    {
        RequireApp();
        await Act(new ElementLocator(ControlSymbol: "IDC_BUTTON_DIALOG"), new ElementAction.Invoke());
        var confirm = Assert.Single(_services.Windows.List(new WindowFilter(TitleContains: "Confirm")).Windows);
        try
        {
            var dialog = Assert.Single(_services.Windows.Inspect(confirm.Hwnd.ToString()).DialogResources!);

            Assert.Equal(("IDD_CONFIRM", 1.0), (dialog.Symbol, dialog.Match));
        }
        finally
        {
            await Act(new ElementLocator(ControlSymbol: "IDCANCEL"), new ElementAction.Invoke(), confirm.Hwnd.ToString());
        }
    }

    [Fact]
    public async Task Inspect_element_guesses_MFC_wrapper_classes()
    {
        RequireApp();

        async Task<string?> Guess(ElementLocator locator) => (await _services.Tree.InspectAsync(Hwnd, null, locator, Token)).MfcClassGuess;

        Assert.Equal("CDialog", await Guess(new ElementLocator(ControlType: "Window")));
        Assert.Equal("CEdit", await Guess(new ElementLocator(ControlSymbol: "IDC_EDIT_NAME")));
        Assert.Equal("CComboBox", await Guess(new ElementLocator(ControlSymbol: "IDC_COMBO_TYPE")));
        Assert.Equal("CListCtrl", await Guess(new ElementLocator(ControlSymbol: "IDC_LIST_ITEMS")));
        Assert.Equal("CTreeCtrl", await Guess(new ElementLocator(ControlSymbol: "IDC_TREE_ITEMS")));
        Assert.Null(await Guess(new ElementLocator(ControlSymbol: "IDC_CANVAS"))); // custom window class
    }

    [Fact]
    public async Task Menu_bar_items_get_distinct_refs_even_without_UIA_runtime_ids()
    {
        RequireApp();

        var tree = await _services.Tree.GetTreeAsync(Hwnd, null, 10, 300, Token);
        var menu = tree.Root.Children!.Single(n => n.ControlType == "MenuBar").Children!;

        Assert.Equal(["File", "Edit", "Help"], menu.Select(m => m.Name));
        Assert.Equal(3, menu.Select(m => m.Ref).Distinct().Count());
        Assert.Equal("Edit", (await _services.Tree.InspectAsync(null, menu[1].Ref, new ElementLocator(), Token)).Name);
    }
}

/// <summary>The statically linked build: detection has to work without mfc140u.dll.</summary>
[Trait("Category", "Windows")]
public sealed class StaticMfcDetectionTests
{
    [Theory]
    [InlineData("", "WinMCP MFC Test App", "executable contains MFC runtime class names (CCmdTarget, CWinThread)")]
    [InlineData("--frame", "WinMCP MFC Frame App", "window class 'AfxFrameOrView140su'")]
    public void Static_MFC_is_detected(string arguments, string title, string evidence)
    {
        Assert.SkipWhen(!File.Exists(MfcAppUnderTest.StaticExePath), "Static MFC variant not built (scripts/build-mfc.ps1).");
        var (process, handle, _) = MfcAppUnderTest.Launch(arguments, title, MfcAppUnderTest.StaticExePath);
        using var _ = process;
        using var services = new WinMcpServices("--allow", "WinMcp.MfcTestApp");
        try
        {
            var framework = services.Windows.Inspect(handle.ToString()).Framework!;

            Assert.Equal(("mfc", "static", null), (framework.Name, framework.Linkage, framework.Version));
            Assert.Contains(evidence, framework.Evidence);
        }
        finally
        {
            process!.Kill();
        }
    }
}

/// <summary>--features: owner-drawn and MFC Feature Pack controls. Documents what works and what stays opaque (M11).</summary>
public sealed class MfcFeaturesSession : IDisposable
{
    private readonly Process? _process;

    public MfcFeaturesSession() => (_process, Handle, SkipReason) = MfcAppUnderTest.Launch("--features", "WinMCP MFC Features");

    public string? SkipReason { get; }

    public WindowHandle Handle { get; }

    public void Dispose()
    {
        if (_process is { HasExited: false })
            _process.Kill();
        _process?.Dispose();
    }
}

[Trait("Category", "Windows")]
public sealed class MfcFeaturePackTests(MfcFeaturesSession app) : IClassFixture<MfcFeaturesSession>, IDisposable
{
    private readonly WinMcpServices _services = new(
        "--mode", "control", "--allow", "WinMcp.MfcTestApp", "--symbols", $"WinMcp.MfcTestApp={MfcAppUnderTest.ResourceHeader}",
        "--audit-dir", Path.Combine(Path.GetTempPath(), $"winmcp-audit-{Guid.NewGuid():N}"));

    public void Dispose() => _services.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string Hwnd => app.Handle.ToString();

    private void RequireApp() => Assert.SkipWhen(app.SkipReason is not null, app.SkipReason ?? "");

    private Task<ActionResult> Act(string symbol, ElementAction action) =>
        _services.Interaction.PerformAsync(Hwnd, null, new ElementLocator(ControlSymbol: symbol), action, Token);

    private Task WaitStatus(string status) =>
        _services.Tree.WaitAsync(Hwnd, null, new ElementLocator(ControlSymbol: "IDC_FEATURES_STATUS"), WaitCondition.TextEquals, status, 5000, Token);

    [Fact]
    public async Task Owner_drawn_and_MFC_buttons_are_ordinary_buttons_named_by_their_window_text()
    {
        RequireApp();
        var ownerDrawn = await _services.Tree.InspectAsync(Hwnd, null, new ElementLocator(ControlSymbol: "IDC_OWNERDRAW_BUTTON"), Token);
        Assert.Equal(("Button", "Owner drawn"), (ownerDrawn.ControlType, ownerDrawn.Name)); // not the painted "Draw me"

        await Act("IDC_OWNERDRAW_BUTTON", new ElementAction.Invoke());
        await WaitStatus("Status: Owner-drawn clicked");
        await Act("IDC_MFC_BUTTON", new ElementAction.Invoke());
        await WaitStatus("Status: MFC button clicked");
    }

    [Fact]
    public async Task Masked_edit_accepts_text_in_its_display_format_only()
    {
        RequireApp();

        var raw = await Act("IDC_MFC_MASKED", new ElementAction.SetValue("5559876543"));
        Assert.False(raw.Changed); // rejected by the mask; reported, not claimed

        var formatted = await Act("IDC_MFC_MASKED", new ElementAction.SetValue("(555) 123-4567"));
        Assert.Equal("5551234567", formatted.ValueAfter); // the control reports its value without the literals
    }

    [Fact]
    public async Task Property_grid_is_recognized_but_its_rows_are_opaque()
    {
        RequireApp();

        var grid = await _services.Tree.InspectAsync(Hwnd, null, new ElementLocator(ControlSymbol: "IDC_MFC_PROPGRID"), Token);
        var subtree = await _services.Tree.GetTreeAsync(null, grid.Ref, 5, 50, Token);

        Assert.Equal("CMFCPropertyGridCtrl", grid.MfcClassGuess);
        Assert.DoesNotContain(OutlineRenderer.Render(subtree), "Alpha"); // property values aren't exposed to UI Automation
        var header = await _services.Tree.InspectAsync(null, subtree.Root.Children!.Single(c => c.ControlType == "Header").Ref, new ElementLocator(), Token);
        Assert.Null(header.ControlSymbol); // its control ID (1) is the grid's, not the dialog's IDOK
    }
}
