using System.Diagnostics;

namespace WinMcp.Testing;

/// <summary>
/// Assembly fixture that ends a GUI test run which exceeds its time budget, so a stuck run fails loudly instead of
/// sitting silently. Budget: <c>WINMCP_TEST_TIMEOUT_MINUTES</c> (default 5; a full run takes well under a minute).
/// Launched applications are cleaned up by <see cref="ChildProcessJob"/> when the process ends.
/// For a dump of where a run is stuck, run with <c>--hangdump --hangdump-timeout 2m</c>.
/// </summary>
public sealed class TestRunWatchdog : IDisposable
{
    private readonly Timer _timer;

    public TestRunWatchdog()
    {
        var minutes = int.TryParse(Environment.GetEnvironmentVariable("WINMCP_TEST_TIMEOUT_MINUTES"), out var configured) && configured > 0
            ? configured
            : 5;
        _timer = new Timer(_ =>
        {
            Console.Error.WriteLine(
                $"WinMCP test run exceeded {minutes} min and is being aborted. Launched test apps are closed with this process. "
                + "Re-run with '--hangdump --hangdump-timeout 2m' to capture where it is stuck.");
            // Kill rather than Environment.Exit/FailFast: exit can block on stuck threads, and FailFast pops Windows Error Reporting.
            Process.GetCurrentProcess().Kill();
        }, null, TimeSpan.FromMinutes(minutes), Timeout.InfiniteTimeSpan);
    }

    public void Dispose() => _timer.Dispose();
}
