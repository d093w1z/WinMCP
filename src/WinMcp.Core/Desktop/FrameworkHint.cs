namespace WinMcp.Core.Desktop;

/// <summary>
/// Guesses the UI framework from window class names. A hint only: class names can be customized,
/// and statically linked MFC in particular may not use <c>Afx</c> classes everywhere.
/// </summary>
public static class FrameworkHint
{
    // First match wins. Checked against the top-level class first, then against child classes.
    private static readonly (Func<string, bool> Matches, string Framework)[] Rules =
    [
        (c => c.StartsWith("WindowsForms10.", StringComparison.Ordinal), "winforms"),
        (c => c.StartsWith("HwndWrapper[", StringComparison.Ordinal), "wpf"),
        (c => c.StartsWith("Afx", StringComparison.Ordinal), "mfc"),
        (c => c == "WinUIDesktopWin32WindowClass", "winui3"),
        (c => c is "ApplicationFrameWindow" or "Windows.UI.Core.CoreWindow", "uwp"),
        (c => c.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal), "chromium"),
        (c => c.StartsWith("Qt5", StringComparison.Ordinal) || c.StartsWith("Qt6", StringComparison.Ordinal) || c.StartsWith("QWidget", StringComparison.Ordinal), "qt"),
        (c => c.StartsWith("SunAwt", StringComparison.Ordinal), "java-awt"),
    ];

    public static string? Guess(string topLevelClass, IEnumerable<string> childClasses) =>
        GuessWithEvidence(topLevelClass, childClasses)?.Framework;

    /// <returns>The framework and the window class that gave it away; null when unrecognized.</returns>
    public static (string Framework, string ClassName)? GuessWithEvidence(string topLevelClass, IEnumerable<string> childClasses)
    {
        if (Match(topLevelClass) is { } top)
            return (top, topLevelClass);
        foreach (var child in childClasses)
        {
            if (Match(child) is { } fromChild)
                return (fromChild, child);
        }
        // A plain dialog class with no framework-specific children: classic Win32 (or an MFC CDialog).
        return topLevelClass == "#32770" ? ("win32-dialog", topLevelClass) : null;
    }

    private static string? Match(string className)
    {
        foreach (var (matches, framework) in Rules)
        {
            if (matches(className))
                return framework;
        }
        return null;
    }
}
