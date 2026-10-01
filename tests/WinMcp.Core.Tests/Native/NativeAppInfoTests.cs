using WinMcp.Core.Desktop;
using WinMcp.Core.Native;
using WinMcp.Core.Policy;
using WinMcp.Core.Symbols;
using WinMcp.Testing;

namespace WinMcp.Core.Tests.Native;

public sealed class NativeAppInfoTests : IDisposable
{
    private const string Exe = @"C:\Apps\Legacy\Legacy.exe";
    private const ushort Button = 0x80, Edit = 0x81, Static = 0x82;

    private static readonly WindowInfo Dialog = FakeDesktop.Window("Legacy", "Settings", pid: 7, hwnd: 0x100, className: "#32770", path: Exe);

    private readonly string _header = Path.Combine(Path.GetTempPath(), $"winmcp-{Guid.NewGuid():N}-resource.h");
    private readonly FakeNativeProcesses _native = new();
    private readonly FakeDesktop _desktop = new(Dialog);
    private readonly NativeAppInfo _info;

    public NativeAppInfoTests()
    {
        File.WriteAllText(_header, """
            #define IDD_SETTINGS     102
            #define IDD_PAGE         103
            #define IDC_EDIT_NAME    1000
            #define IDC_EDIT_HIDDEN  1001
            #define IDC_EDIT_EXTRA   1101
            """);
        var options = WinMcpOptions.Parse(["--allow", "Legacy", "--symbols", $"Legacy={_header}"]);
        _info = new NativeAppInfo(_native, new SymbolProvider(options));

        _native.Modules[7] = [FakeNativeProcesses.Module(Exe), FakeNativeProcesses.Module(@"C:\Windows\System32\mfc140u.dll")];
        _native.Dialogs[Exe] = [Resource(Exe, 102, "Settings", (-1, Static, "Name:"), (1000, Edit, ""), (1001, Edit, ""), (1, Button, "OK"))];
        _desktop.Children[Dialog.Hwnd] =
        [
            FakeDesktop.Child(0x101, 0x100, "Static", "Name:", controlId: 0xFFFF),
            FakeDesktop.Child(0x102, 0x100, "Edit", "", controlId: 1000),
            FakeDesktop.Child(0x103, 0x100, "Edit", "", controlId: 1001) with { Visible = false },
            FakeDesktop.Child(0x104, 0x100, "Button", "OK", controlId: 1),
            FakeDesktop.Child(0x105, 0x100, "Edit", "", controlId: 1101),
        ];
    }

    public void Dispose() => File.Delete(_header);

    private static DialogResource Resource(string module, int id, string caption, params (int, object, string)[] items) =>
        new(module, id, null, DialogTemplateBuilder.Extended(caption, items));

    private IReadOnlyList<DialogResourceInfo> Dialogs(WindowInfo window) =>
        _info.Dialogs(FakeDesktop.DefaultDetails(window), _desktop.GetChildWindows(window.Hwnd, includeHidden: true));

    [Fact]
    public void Matches_the_dialog_template_with_symbols_extra_and_hidden_controls()
    {
        var match = Assert.Single(Dialogs(Dialog));

        Assert.Equal((Dialog.Hwnd, 102, "IDD_SETTINGS", "Legacy.exe", "Settings"), (match.Hwnd, match.ResourceId, match.Symbol, match.Module, match.TemplateCaption));
        Assert.Equal([new ControlRef(1101, "IDC_EDIT_EXTRA")], match.ExtraControls);
        Assert.Equal([new ControlRef(1001, "IDC_EDIT_HIDDEN")], match.HiddenControls);
        Assert.Empty(match.MissingControls);
        Assert.Null(match.Alternatives);
    }

    [Fact]
    public void Child_dialogs_are_matched_too_and_satellite_or_app_dlls_are_searched()
    {
        var frame = FakeDesktop.Window("Legacy", "Legacy", pid: 7, hwnd: 0x200, className: "AfxFrameOrView140u", path: Exe);
        _desktop.Children[frame.Hwnd] =
        [
            FakeDesktop.Child(0x201, 0x200, "#32770", "", controlId: 0xE900),
            FakeDesktop.Child(0x202, 0x201, "Edit", "", controlId: 2000),
            FakeDesktop.Child(0x203, 0x201, "Edit", "", controlId: 2001),
        ];
        const string satellite = @"C:\Apps\Legacy\1033\LegacyUI.dll";
        _native.Satellites[Exe] = [satellite];
        _native.Dialogs[satellite] = [Resource(satellite, 103, "", (2000, Edit, ""), (2001, Edit, ""))];

        var match = Assert.Single(Dialogs(frame));

        Assert.Equal((new WindowHandle(0x201), "IDD_PAGE", "LegacyUI.dll"), (match.Hwnd, match.Symbol, match.Module));
    }

    [Fact]
    public void Only_the_applications_own_dlls_are_searched()
    {
        const string appDll = @"C:\Apps\Legacy\Plugins.dll", systemDll = @"C:\Windows\System32\comdlg32.dll";
        _native.Modules[7].AddRange([FakeNativeProcesses.Module(appDll), FakeNativeProcesses.Module(systemDll)]);
        _native.Dialogs[Exe] = [];
        // The system DLL's template would match just as well; it isn't the application's.
        _native.Dialogs[systemDll] = [Resource(systemDll, 1536, "Settings", (1000, Edit, ""), (1001, Edit, ""), (1, Button, "OK"))];
        _native.Dialogs[appDll] = [Resource(appDll, 300, "Settings", (1000, Edit, ""), (1001, Edit, ""), (1, Button, "OK"))];

        var match = Assert.Single(Dialogs(Dialog));

        Assert.Equal(("Plugins.dll", 300), (match.Module, match.ResourceId));
        Assert.Null(match.Alternatives);
    }

    [Fact]
    public void Framework_uses_modules_and_caches_them_per_process()
    {
        var details = FakeDesktop.DefaultDetails(Dialog);

        Assert.Equal(("mfc", "shared"), (_info.Framework(details)!.Name, _info.Framework(details)!.Linkage));
        Assert.True(_info.IsMfc(details));
        Assert.Equal(1, _native.ModuleCalls);
    }

    [Fact]
    public void Static_MFC_executable_is_scanned_once()
    {
        _native.Modules[7] = [FakeNativeProcesses.Module(Exe)];
        _native.StaticMfcExecutables.Add(Exe);
        var details = FakeDesktop.DefaultDetails(Dialog);

        Assert.Equal("static", _info.Framework(details)!.Linkage);
        Assert.Equal("static", _info.Framework(details)!.Linkage);
        Assert.Equal(1, _native.MarkerScans);
    }

    [Fact]
    public void MFC_processes_get_MFC_standard_symbols_even_without_resource_h()
    {
        var info = new NativeAppInfo(_native, new SymbolProvider(WinMcpOptions.Parse(["--allow", "Legacy"])));

        var table = info.Symbols(FakeDesktop.DefaultDetails(Dialog))!;

        Assert.Equal("AFX_IDW_STATUS_BAR", table.LookupControl(0xE801).Symbol);
        Assert.Equal("ID_APP_EXIT", table.LookupCommand(0xE141).Symbol);
    }
}
