using System.Text.RegularExpressions;
using WinMcp.Core.Desktop;

namespace WinMcp.Core.Native;

/// <summary>The UI framework of a window's application, with the facts it was inferred from.</summary>
/// <param name="Name">As <see cref="FrameworkHint"/>: <c>mfc</c>, <c>winforms</c>, <c>win32-dialog</c>, …</param>
/// <param name="Linkage">MFC only: <c>shared</c> (MFC DLL loaded) or <c>static</c> (MFC window classes, no MFC DLL).</param>
/// <param name="Version">Version of the framework DLL, when one was found.</param>
/// <param name="Evidence">Human-readable facts, e.g. <c>loaded module mfc140u.dll</c>.</param>
/// <param name="Libraries">MFC extension libraries recognized by window classes or DLLs (e.g. <c>BCGControlBar</c>).</param>
public sealed record FrameworkInfo(string Name, string? Linkage, string? Version, IReadOnlyList<string> Evidence, IReadOnlyList<string>? Libraries = null);

/// <summary>
/// Combines loaded modules (reliable for shared MFC: <c>mfc140u.dll</c> and relatives) with window class names
/// (<see cref="FrameworkHint"/>; the only signal for statically linked MFC, and then only when the app uses
/// <c>Afx</c> window classes — a static MFC dialog app looks exactly like plain Win32).
/// </summary>
public static partial class FrameworkDetection
{
    /// <summary>mfc140u.dll, mfc140ud.dll (debug), mfc140.dll (MBCS), mfc42u.dll, …</summary>
    [GeneratedRegex(@"^mfc\d+u?d?\.dll$", RegexOptions.IgnoreCase)]
    private static partial Regex MfcModule();

    /// <summary>Static MFC's registered classes carry an "s" in their version suffix, e.g. <c>AfxWnd140su</c>.</summary>
    [GeneratedRegex(@"^Afx[A-Za-z]+\d+s", RegexOptions.None)]
    private static partial Regex StaticMfcClass();

    /// <summary>
    /// Names of MFC's <c>CRuntimeClass</c> objects that every MFC program contains; compiled into the executable only
    /// when MFC is linked statically (shared MFC keeps them in <c>mfc140u.dll</c>).
    /// </summary>
    public static readonly IReadOnlyList<string> StaticMfcMarkers = ["CCmdTarget", "CWinThread"];

    /// <param name="modules">Null when the process couldn't be read.</param>
    /// <param name="fileVersion">Version lookup for a module file (only called for the framework DLL).</param>
    /// <param name="executableHasMfcClasses">
    /// Whether the executable embeds <see cref="StaticMfcMarkers"/>: the only sign of a statically linked MFC app that
    /// uses no <c>Afx</c> window classes (a plain dialog app). Only asked when nothing else decided.
    /// </param>
    public static FrameworkInfo? Detect(
        string topLevelClass, IReadOnlyList<string> childClasses, IReadOnlyList<LoadedModule>? modules, Func<string, string?> fileVersion,
        Func<bool>? executableHasMfcClasses = null)
    {
        var info = DetectFramework(topLevelClass, childClasses, modules, fileVersion, executableHasMfcClasses);
        if (info?.Name != "mfc")
            return info;
        var libraries = MfcLibraryRules
            .Select(rule => (rule.Name, Evidence: LibraryEvidence(rule, topLevelClass, childClasses, modules)))
            .Where(l => l.Evidence is not null)
            .ToList();
        return libraries.Count == 0 ? info : info with
        {
            Evidence = [.. info.Evidence, .. libraries.Select(l => l.Evidence!)],
            Libraries = libraries.Select(l => l.Name).ToList(),
        };
    }

    /// <summary>
    /// Commercial MFC extension libraries replace MFC's toolbars, ribbons and docking panes with their own window classes
    /// (a real-world application: <c>BCGPRibbonBar</c>, <c>BCGPControlBar</c>, …, M11). Knowing the library explains
    /// what UI Automation shows (BCG splitters appear as unnamed tool bars) and which controls are custom-drawn.
    /// </summary>
    private static readonly (string Name, string ClassPrefix, string ModulePrefix)[] MfcLibraryRules =
    [
        ("BCGControlBar", "BCGP", "BCGCB"),
        ("Codejock Xtreme Toolkit", "XTP", "ToolkitPro"),
    ];

    private static string? LibraryEvidence(
        (string Name, string ClassPrefix, string ModulePrefix) rule, string topLevelClass, IReadOnlyList<string> childClasses, IReadOnlyList<LoadedModule>? modules)
    {
        if (modules?.FirstOrDefault(m => m.Name.StartsWith(rule.ModulePrefix, StringComparison.OrdinalIgnoreCase)) is { } module)
            return $"{rule.Name}: loaded module {module.Name}";
        var windowClass = childClasses.Prepend(topLevelClass).FirstOrDefault(c => c.StartsWith(rule.ClassPrefix, StringComparison.Ordinal));
        return windowClass is null ? null : $"{rule.Name}: window class '{windowClass}'";
    }

    private static FrameworkInfo? DetectFramework(
        string topLevelClass, IReadOnlyList<string> childClasses, IReadOnlyList<LoadedModule>? modules, Func<string, string?> fileVersion,
        Func<bool>? executableHasMfcClasses)
    {
        var byClass = FrameworkHint.GuessWithEvidence(topLevelClass, childClasses);
        var classEvidence = byClass is { } c ? $"window class '{c.ClassName}'" : null;

        if (modules?.FirstOrDefault(m => MfcModule().IsMatch(m.Name)) is { } mfc)
        {
            var version = fileVersion(mfc.Path);
            List<string> evidence = [$"loaded module {mfc.Name}" + (version is null ? "" : $" ({version})")];
            if (byClass?.Framework == "mfc")
                evidence.Add(classEvidence!);
            return new FrameworkInfo("mfc", "shared", version, evidence);
        }

        // Readable process, no MFC DLL, nothing MFC-like among the windows: static MFC is still possible (M11: a static
        // MFC dialog app looked exactly like Win32). Its executable then contains MFC's own class names.
        if (modules is not null && byClass?.Framework is null or "win32-dialog" && executableHasMfcClasses?.Invoke() == true)
        {
            List<string> evidence = ["executable contains MFC runtime class names (CCmdTarget, CWinThread)", "no MFC DLL loaded"];
            if (classEvidence is not null)
                evidence.Add(classEvidence);
            return new FrameworkInfo("mfc", "static", null, evidence);
        }

        if (byClass is not { } found)
            return null;
        if (found.Framework != "mfc")
            return new FrameworkInfo(found.Framework, null, null, [classEvidence!]);

        // MFC window classes without an MFC DLL: statically linked (or modules unreadable).
        var linkage = modules is not null || StaticMfcClass().IsMatch(found.ClassName) ? "static" : null;
        List<string> staticEvidence = [classEvidence!];
        if (modules is not null)
            staticEvidence.Add("no MFC DLL loaded");
        return new FrameworkInfo("mfc", linkage, null, staticEvidence);
    }
}
