using Windows.Win32;
using Windows.Win32.UI.HiDpi;

namespace WinMcp.Windows;

/// <summary>
/// Runs Win32/UIA geometry calls in physical pixels regardless of the host process's DPI awareness.
/// Without it, a DPI-unaware host (e.g. a test runner without a manifest) gets scaled results from GetWindowRect and
/// UI Automation while DWM keeps reporting physical pixels — mixing the two broke bounds and captures at 150% (M8).
/// </summary>
internal readonly struct DpiScope : IDisposable
{
    private static readonly DPI_AWARENESS_CONTEXT PerMonitorV2 = (DPI_AWARENESS_CONTEXT)(nint)(-4); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

    private readonly DPI_AWARENESS_CONTEXT _previous;

    private DpiScope(DPI_AWARENESS_CONTEXT previous) => _previous = previous;

    /// <summary>Switches the calling thread to Per-Monitor-V2 until disposed.</summary>
    public static DpiScope Enter() => new(PInvoke.SetThreadDpiAwarenessContext(PerMonitorV2));

    /// <summary>For threads WinMCP owns: switch permanently.</summary>
    public static void SetForCurrentThread() => PInvoke.SetThreadDpiAwarenessContext(PerMonitorV2);

    public void Dispose()
    {
        if (_previous != default)
            PInvoke.SetThreadDpiAwarenessContext(_previous);
    }
}
