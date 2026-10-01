using WinMcp.Core.Desktop;
using WinMcp.Core.Symbols;

namespace WinMcp.Core.Native;

/// <summary>A control ID with its <c>resource.h</c>/MFC name, when known.</summary>
public sealed record ControlRef(int Id, string? Symbol);

/// <summary>
/// The dialog template a live dialog (a <c>#32770</c> window: the window itself, a property page, a form view) was
/// created from — design-time identity that UI Automation doesn't have.
/// </summary>
/// <param name="ResourceId">Numeric template ID; null for string-named templates (see <paramref name="ResourceName"/>).</param>
/// <param name="Symbol"><c>IDD_*</c> name from the configured <c>resource.h</c>.</param>
/// <param name="Module">File the template lives in (the EXE, an app DLL, or a satellite resource DLL).</param>
/// <param name="Match">Control-ID similarity, 0.5–1 (1 = the same controls).</param>
/// <param name="ExtraControls">Live controls not in the template: created at run time.</param>
/// <param name="MissingControls">Template controls not present: destroyed or never created.</param>
/// <param name="HiddenControls">Template controls that exist but are hidden (not in the UI tree).</param>
/// <param name="Alternatives">Other equally good templates (e.g. identical dialogs); null when the match is unique.</param>
public sealed record DialogResourceInfo(
    WindowHandle Hwnd,
    int? ResourceId,
    string? ResourceName,
    string? Symbol,
    string Module,
    double Match,
    string TemplateCaption,
    IReadOnlyList<ControlRef> ExtraControls,
    IReadOnlyList<ControlRef> MissingControls,
    IReadOnlyList<ControlRef> HiddenControls,
    IReadOnlyList<string>? Alternatives);

/// <summary>
/// Facts about a target application beyond its windows (M11): the framework from loaded modules, and dialog templates
/// from its files. Everything is read-only and out of process; results are cached briefly per process and per file.
/// </summary>
public sealed class NativeAppInfo(INativeProcesses native, SymbolProvider symbols)
{
    private static readonly TimeSpan ModuleCacheTime = TimeSpan.FromSeconds(30);
    private const int MaxTemplateModules = 24;

    private readonly Lock _lock = new();
    private readonly Dictionary<int, (string? Path, DateTime At, IReadOnlyList<LoadedModule>? Modules)> _modules = [];
    private readonly Dictionary<string, IReadOnlyList<DialogTemplate>> _templates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _staticMfc = new(StringComparer.OrdinalIgnoreCase);

    public FrameworkInfo? Framework(WindowDetails window) =>
        FrameworkDetection.Detect(window.Window.ClassName, window.ChildClassNames, Modules(window.Window.Process), native.GetFileVersion,
            () => HasStaticMfcMarkers(window.Window.Process));

    public bool IsMfc(WindowDetails window) => Framework(window)?.Name == "mfc";

    /// <summary>Symbols for a window's process: the configured <c>resource.h</c>, plus MFC's standard IDs for MFC apps.</summary>
    public SymbolTable? Symbols(WindowDetails window) => symbols.ForProcess(window.Window.Process.Name, IsMfc(window));

    /// <summary>Template matches for the window, if it is a dialog, and for the dialogs inside it.</summary>
    public IReadOnlyList<DialogResourceInfo> Dialogs(WindowDetails window, IReadOnlyList<ChildWindow> children)
    {
        var dialogs = new List<(WindowHandle Hwnd, string Caption)>();
        if (window.Window.ClassName == "#32770")
            dialogs.Add((window.Window.Hwnd, window.Window.Title));
        dialogs.AddRange(children.Where(c => c.ClassName == "#32770").Select(c => (c.Hwnd, c.Text)));
        if (dialogs.Count == 0)
            return [];

        var templates = Templates(window.Window.Process);
        if (templates.Count == 0)
            return [];
        var table = Symbols(window);

        var results = new List<DialogResourceInfo>();
        foreach (var (hwnd, caption) in dialogs)
        {
            var own = children.Where(c => c.Parent == hwnd).ToList();
            var ids = own.Select(c => c.ControlId).ToList();
            if (DialogMatcher.Best(ids, caption, templates) is not { } match)
                continue;
            var t = match.Template;
            results.Add(new DialogResourceInfo(
                hwnd,
                t.Id,
                t.Name,
                t.Id is { } id ? table?.LookupDialog(id).Symbol : null,
                Path.GetFileName(t.Module),
                match.Score,
                t.Caption,
                match.Extra.Select(i => Ref(table, i)).ToList(),
                match.Missing.Select(i => Ref(table, i)).ToList(),
                own.Where(c => !c.Visible && t.Items.Any(i => i.Id == c.ControlId && DialogMatcher.IsSignificant(i.Id)))
                    .Select(c => Ref(table, c.ControlId)).ToList(),
                match.Alternatives.Count == 0 ? null : match.Alternatives.Select(a => a.Id is { } aid ? $"#{aid}" : a.Name ?? "?").ToList()));
        }
        return results;
    }

    private bool HasStaticMfcMarkers(ProcessInfo process)
    {
        if (process.Path is not { } exe)
            return false;
        var key = $"{exe}|{SafeStamp(exe):O}";
        lock (_lock)
        {
            if (_staticMfc.TryGetValue(key, out var cached))
                return cached;
        }
        var found = native.FileContainsAsciiStrings(exe, FrameworkDetection.StaticMfcMarkers);
        lock (_lock)
            _staticMfc[key] = found;
        return found;
    }

    private static ControlRef Ref(SymbolTable? table, int id) => new(id, table?.LookupControl(id).Symbol);

    private IReadOnlyList<LoadedModule>? Modules(ProcessInfo process)
    {
        lock (_lock)
        {
            if (_modules.TryGetValue(process.Pid, out var cached) && cached.Path == process.Path && DateTime.UtcNow - cached.At < ModuleCacheTime)
                return cached.Modules;
        }
        var modules = native.GetModules(process.Pid);
        lock (_lock)
            _modules[process.Pid] = (process.Path, DateTime.UtcNow, modules);
        return modules;
    }

    /// <summary>
    /// Templates from the EXE, the application's own DLLs (loaded from its folder) and its satellite resource DLLs.
    /// System DLLs are skipped: their dialogs (common dialogs, message boxes) aren't the application's.
    /// </summary>
    private List<DialogTemplate> Templates(ProcessInfo process)
    {
        if (process.Path is not { } exe)
            return [];
        var folder = Path.GetDirectoryName(exe) ?? "";
        var files = new List<string> { exe };
        files.AddRange((Modules(process) ?? [])
            .Select(m => m.Path)
            .Where(p => !string.Equals(p, exe, StringComparison.OrdinalIgnoreCase)
                        && p.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
        files.AddRange(native.GetSatelliteModules(exe));

        var templates = new List<DialogTemplate>();
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxTemplateModules))
            templates.AddRange(TemplatesOf(file));
        return templates;
    }

    private IReadOnlyList<DialogTemplate> TemplatesOf(string file)
    {
        var key = $"{file}|{SafeStamp(file):O}";
        lock (_lock)
        {
            if (_templates.TryGetValue(key, out var cached))
                return cached;
        }
        var parsed = native.GetDialogResources(file).Select(DialogTemplateParser.Parse).OfType<DialogTemplate>().ToList();
        lock (_lock)
            _templates[key] = parsed;
        return parsed;
    }

    private static DateTime SafeStamp(string file)
    {
        try { return File.GetLastWriteTimeUtc(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return default; }
    }
}
