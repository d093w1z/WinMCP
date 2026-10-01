using System.Diagnostics;
using System.Reflection;
using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>
/// One test app as the cross-app scenarios see it: how to start WinMCP for it and how to address each logical control.
/// The WinForms and MFC apps implement the same contract (same statuses and event names), located differently:
/// WinForms by automation_id, MFC by control_symbol from its resource.h.
/// </summary>
public interface IAppUnderTest
{
    /// <summary>Null when the app can run; otherwise why its tests are skipped.</summary>
    string? SkipReason { get; }

    int ProcessId { get; }

    WindowHandle Handle { get; }

    string[] ControlModeArguments(string auditDirectory);

    /// <summary>Locator for a logical control: name, type, enable, apply, cancel, advanced, slowApply, addField, freeze,
    /// dialog, status, events, list, tree, dynamic1, dialogNote, dialogOk.</summary>
    ElementLocator Locate(string control);

    /// <summary>Tab to select before the tree view is visible, if any.</summary>
    (ElementLocator Tabs, string Tab)? TreeTab { get; }
}

/// <summary>samples/WinMcp.TestApp (WinForms).</summary>
public sealed class WinFormsAppUnderTest : IAppUnderTest, IDisposable
{
    private static readonly Dictionary<string, string> AutomationIds = new()
    {
        ["name"] = "nameTextBox", ["type"] = "typeComboBox", ["enable"] = "enableCheckBox", ["apply"] = "applyButton",
        ["cancel"] = "cancelButton", ["advanced"] = "advancedButton", ["slowApply"] = "slowApplyButton",
        ["addField"] = "addFieldButton", ["freeze"] = "freezeButton", ["dialog"] = "dialogButton", ["status"] = "statusLabel",
        ["events"] = "eventLogLabel", ["list"] = "itemsListView", ["tree"] = "itemsTreeView", ["dynamic1"] = "dynamicTextBox1",
        ["dialogNote"] = "dialogNoteTextBox", ["dialogOk"] = "dialogOkButton",
    };

    private readonly TestAppSession _session = new();

    public string? SkipReason => null;

    public int ProcessId => _session.ProcessId;

    public WindowHandle Handle => _session.WindowHandle;

    public string[] ControlModeArguments(string auditDirectory) =>
        ["--mode", "control", "--allow", "WinMcp.TestApp", "--audit-dir", auditDirectory];

    public ElementLocator Locate(string control) => new(AutomationId: AutomationIds[control]);

    public (ElementLocator Tabs, string Tab)? TreeTab => (new ElementLocator(AutomationId: "detailsTabs"), "Tree");

    public override string ToString() => "WinForms";

    public void Dispose() => _session.Dispose();
}

/// <summary>samples/WinMcp.MfcTestApp (C++/MFC dialog). Built by scripts/build-mfc.ps1; skipped when not built.</summary>
public sealed class MfcAppUnderTest : IAppUnderTest, IDisposable
{
    private static readonly Dictionary<string, string> Symbols = new()
    {
        ["name"] = "IDC_EDIT_NAME", ["type"] = "IDC_COMBO_TYPE", ["enable"] = "IDC_CHECK_ENABLE", ["apply"] = "IDC_BUTTON_APPLY",
        ["cancel"] = "IDC_BUTTON_CANCEL", ["advanced"] = "IDC_BUTTON_ADVANCED", ["slowApply"] = "IDC_BUTTON_SLOW_APPLY",
        ["addField"] = "IDC_BUTTON_ADD_FIELD", ["freeze"] = "IDC_BUTTON_FREEZE", ["dialog"] = "IDC_BUTTON_DIALOG",
        ["status"] = "IDC_STATIC_STATUS", ["events"] = "IDC_STATIC_EVENTS", ["list"] = "IDC_LIST_ITEMS", ["tree"] = "IDC_TREE_ITEMS",
        ["dynamic1"] = "IDC_EDIT_DYNAMIC1", ["dialogNote"] = "IDC_EDIT_NOTE",
    };

    private readonly Process? _process;

    public MfcAppUnderTest() => (_process, Handle, SkipReason) = Launch("", "WinMCP MFC Test App");

    /// <summary>Starts the MFC test app and waits for its main window; returns a skip reason instead when it isn't built.</summary>
    internal static (Process? Process, WindowHandle Handle, string? SkipReason) Launch(string arguments, string title, string? exePath = null)
    {
        var path = Path.GetFullPath(exePath ?? ExePath);
        if (!File.Exists(path))
            return (null, default, $"MFC test app not built ({path}). Run scripts/build-mfc.ps1 or set WINMCP_MFCTESTAPP_PATH.");

        var process = Process.Start(path, $"--position 200,200 {arguments}".Trim());
        WinMcp.Testing.ChildProcessJob.Add(process.Id);
        process.WaitForInputIdle(15_000);
        var desktop = new Win32Desktop();
        WindowInfo? window = null;
        TestAppSession.WaitUntil(
            () => (window = desktop.GetTopLevelWindows().FirstOrDefault(w => w.Process.Pid == process.Id && w.Title == title && w.Visible)) is not null,
            $"the '{title}' window appears", TimeSpan.FromSeconds(15));
        return (process, window!.Hwnd, null);
    }

    public static string ExePath =>
        Environment.GetEnvironmentVariable("WINMCP_MFCTESTAPP_PATH") is { Length: > 0 } overridePath ? overridePath : BuiltExePath;

    /// <summary>The statically linked MFC variant (scripts/build-mfc.ps1 builds both).</summary>
    public static string StaticExePath => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(BuiltExePath)!, @"..\ReleaseStatic\WinMcp.MfcTestApp.exe"));

    /// <summary>The app's resource.h in the source tree (the build output is bin\x64\Release below it).</summary>
    public static string ResourceHeader => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(BuiltExePath)!, @"..\..\..\resource.h"));

    private static string BuiltExePath =>
        typeof(MfcAppUnderTest).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "MfcTestAppPath").Value!;

    public string? SkipReason { get; }

    public int ProcessId => _process!.Id;

    public WindowHandle Handle { get; }

    public string[] ControlModeArguments(string auditDirectory) =>
        ["--mode", "control", "--allow", "WinMcp.MfcTestApp", "--symbols", $"WinMcp.MfcTestApp={ResourceHeader}", "--audit-dir", auditDirectory];

    // IDOK comes from winuser.h, not the app's resource.h, so the dialog's OK button is found by name.
    public ElementLocator Locate(string control) =>
        control == "dialogOk" ? new ElementLocator(Name: "OK", ControlType: "Button") : new ElementLocator(ControlSymbol: Symbols[control]);

    public (ElementLocator Tabs, string Tab)? TreeTab => null;

    public override string ToString() => "MFC";

    public void Dispose()
    {
        if (_process is { HasExited: false })
            _process.Kill();
        _process?.Dispose();
    }
}
