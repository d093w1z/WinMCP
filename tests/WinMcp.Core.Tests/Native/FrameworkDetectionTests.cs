using WinMcp.Core.Mfc;
using WinMcp.Core.Native;
using WinMcp.Testing;

namespace WinMcp.Core.Tests.Native;

public sealed class FrameworkDetectionTests
{
    private static readonly LoadedModule[] Win32Modules = [FakeNativeProcesses.Module(@"C:\Windows\System32\user32.dll")];
    private static readonly LoadedModule[] MfcModules = [.. Win32Modules, FakeNativeProcesses.Module(@"C:\Windows\System32\mfc140u.dll")];

    private static string? Version(string path) => path.EndsWith("mfc140u.dll", StringComparison.Ordinal) ? "14.44.35207.1" : null;

    [Fact]
    public void Loaded_MFC_DLL_means_shared_MFC_with_its_version()
    {
        var info = FrameworkDetection.Detect("#32770", ["Button", "Edit"], MfcModules, Version)!;

        Assert.Equal(("mfc", "shared", "14.44.35207.1"), (info.Name, info.Linkage, info.Version));
        Assert.Equal(["loaded module mfc140u.dll (14.44.35207.1)"], info.Evidence);
    }

    [Theory]
    [InlineData("mfc140ud.dll")]
    [InlineData("MFC140.dll")]
    [InlineData("mfc42u.dll")]
    public void Recognizes_MFC_DLL_variants(string dll) =>
        Assert.Equal("shared", FrameworkDetection.Detect("#32770", [], [FakeNativeProcesses.Module($@"C:\x\{dll}")], _ => null)!.Linkage);

    [Fact]
    public void Afx_classes_without_an_MFC_DLL_mean_static_MFC()
    {
        var info = FrameworkDetection.Detect("AfxFrameOrView140su", [], Win32Modules, Version)!;

        Assert.Equal(("mfc", "static"), (info.Name, info.Linkage));
        Assert.Equal(["window class 'AfxFrameOrView140su'", "no MFC DLL loaded"], info.Evidence);
    }

    [Fact]
    public void Unreadable_modules_leave_linkage_open_unless_the_class_says_static()
    {
        Assert.Null(FrameworkDetection.Detect("AfxFrameOrView140u", [], modules: null, Version)!.Linkage);
        Assert.Equal("static", FrameworkDetection.Detect("AfxWnd140su", [], modules: null, Version)!.Linkage);
    }

    [Fact]
    public void Static_MFC_dialog_app_is_recognized_by_its_executable()
    {
        var info = FrameworkDetection.Detect("#32770", ["Button"], Win32Modules, Version, () => true)!;

        Assert.Equal(("mfc", "static"), (info.Name, info.Linkage));
        Assert.Contains("executable contains MFC runtime class names (CCmdTarget, CWinThread)", info.Evidence);
    }

    [Fact]
    public void Executable_is_only_scanned_when_nothing_else_decided()
    {
        var scans = 0;
        bool Scan() { scans++; return true; }

        Assert.Equal("shared", FrameworkDetection.Detect("#32770", [], MfcModules, Version, Scan)!.Linkage);
        Assert.Equal("winforms", FrameworkDetection.Detect("WindowsForms10.Window.8.app.0.1", [], Win32Modules, Version, Scan)!.Name);
        Assert.Equal("win32-dialog", FrameworkDetection.Detect("#32770", [], modules: null, Version, Scan)!.Name);
        Assert.Equal(0, scans);
    }

    [Fact]
    public void Other_frameworks_come_from_class_names_with_evidence()
    {
        var info = FrameworkDetection.Detect("MainWnd", ["WindowsForms10.BUTTON.app.0.1"], Win32Modules, Version, () => false)!;

        Assert.Equal(("winforms", null), (info.Name, info.Linkage));
        Assert.Equal(["window class 'WindowsForms10.BUTTON.app.0.1'"], info.Evidence);
        Assert.Null(FrameworkDetection.Detect("Notepad", ["Edit"], Win32Modules, Version, () => false));
    }
}

public sealed class MfcClassGuessTests
{
    [Theory]
    [InlineData("#32770", null, true, "CDialog")]
    [InlineData("#32770", 0xE900, false, "CFormView")]
    [InlineData("#32770", 0xE805, false, "CDialogBar")]
    [InlineData("Button", 1003, false, "CButton")]
    [InlineData("SysListView32", 1012, false, "CListCtrl")]
    [InlineData("SysTreeView32", 1013, false, "CTreeCtrl")]
    [InlineData("ToolbarWindow32", 0xE800, false, "CToolBar")]
    [InlineData("ToolbarWindow32", 2000, false, "CToolBar or CToolBarCtrl")]
    [InlineData("msctls_statusbar32", 0xE801, false, "CStatusBar")]
    [InlineData("AfxFrameOrView140u", null, true, "CFrameWnd")]
    [InlineData("AfxFrameOrView140u", 0xE900, false, "CView")]
    [InlineData("Afx:00400000:8:00010003:00000000:00000000", null, true, "CFrameWnd")]
    [InlineData("AfxMDIFrame140u", null, true, "CMDIFrameWnd")]
    [InlineData("AfxControlBar140u", 0xE81B, false, "CDockBar")]
    [InlineData("Afx:PropList:1bb50000:8:10003:10", 1306, false, "CMFCPropertyGridCtrl")]
    [InlineData("AfxWnd140u", 1014, false, "CWnd")]
    [InlineData("WinMcpCanvas", 1014, false, null)]
    public void Guesses_the_wrapper_class(string className, int? controlId, bool topLevel, string? expected) =>
        Assert.Equal(expected, MfcClassGuess.Guess(className, controlId, topLevel));

    [Fact]
    public void Control_id_of_a_top_level_window_is_ignored() =>
        Assert.Equal("CDialog", MfcClassGuess.Guess("#32770", 0xE900, topLevel: true));
}
