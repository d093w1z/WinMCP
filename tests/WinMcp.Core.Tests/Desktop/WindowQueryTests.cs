using WinMcp.Core.Desktop;
using WinMcp.Core.Errors;
using WinMcp.Core.Policy;
using WinMcp.Testing;

namespace WinMcp.Core.Tests.Desktop;

public sealed class WindowQueryTests
{
    private static readonly WindowInfo TestApp = FakeDesktop.Window("WinMcp.TestApp", "WinMCP Test App", pid: 1, hwnd: 0x10);
    private static readonly WindowInfo TestAppDialog = FakeDesktop.Window("WinMcp.TestApp", "Options", pid: 1, hwnd: 0x11);
    private static readonly WindowInfo Secret = FakeDesktop.Window("mail", "Password reset for bob", pid: 2, hwnd: 0x20);
    private static readonly WindowInfo Browser = FakeDesktop.Window("browser", "Bank", pid: 3, hwnd: 0x30);
    private static readonly WindowInfo HiddenTestApp = FakeDesktop.Window("WinMcp.TestApp", "Hidden", pid: 1, hwnd: 0x12, visible: false);
    private static readonly WindowInfo CloakedTestApp = FakeDesktop.Window("WinMcp.TestApp", "Cloaked", pid: 1, hwnd: 0x13, cloaked: true);
    private static readonly WindowInfo HiddenOther = FakeDesktop.Window("mail", "Hidden mail", pid: 2, hwnd: 0x21, visible: false);

    private static WindowQuery Query(params string[] allow) =>
        new(new FakeDesktop(TestApp, Secret, TestAppDialog, Browser, HiddenTestApp, CloakedTestApp, HiddenOther),
            new TargetPolicy(new WinMcpOptions(ServerMode.Observe, allow), ownPid: 999));

    [Fact]
    public void Returns_only_allowlisted_shown_windows_in_z_order()
    {
        var result = Query("WinMcp.TestApp").List(new WindowFilter());

        Assert.Equal([TestApp, TestAppDialog], result.Windows);
        Assert.Equal(2, result.ExcludedCount); // Secret, Browser — hidden windows aren't counted
        Assert.Null(result.Hint);
    }

    [Fact]
    public void Include_hidden_adds_hidden_and_cloaked_windows_and_counts_hidden_exclusions()
    {
        var result = Query("WinMcp.TestApp").List(new WindowFilter(IncludeHidden: true));

        Assert.Equal([TestApp, TestAppDialog, HiddenTestApp, CloakedTestApp], result.Windows);
        Assert.Equal(3, result.ExcludedCount);
    }

    [Theory]
    [InlineData("WinMcp.TestApp", null, null, 2)]
    [InlineData("winmcp.testapp.exe", null, null, 2)]
    [InlineData(null, "OPTIONS", null, 1)]
    [InlineData(null, null, 1, 2)]
    [InlineData(null, null, 7, 0)]
    public void Filters_apply_to_allowlisted_windows(string? processName, string? titleContains, int? pid, int expected) =>
        Assert.Equal(expected, Query("WinMcp.TestApp").List(new WindowFilter(processName, titleContains, pid)).Windows.Count);

    [Theory]
    [InlineData("mail", null, null)]
    [InlineData(null, "password", null)]
    [InlineData(null, null, 2)]
    public void Filters_cannot_probe_non_allowlisted_windows(string? processName, string? titleContains, int? pid)
    {
        var unfiltered = Query("WinMcp.TestApp").List(new WindowFilter());
        var probed = Query("WinMcp.TestApp").List(new WindowFilter(processName, titleContains, pid));

        Assert.Empty(probed.Windows);
        Assert.Equal(unfiltered.ExcludedCount, probed.ExcludedCount);
    }

    [Fact]
    public void Empty_allowlist_explains_why_nothing_is_visible()
    {
        var result = Query().List(new WindowFilter());

        Assert.Empty(result.Windows);
        Assert.Equal(4, result.ExcludedCount);
        Assert.Contains("--allow", result.Hint);
    }

    [Fact]
    public void Empty_result_hint_names_the_allowlist() =>
        Assert.Contains("WinMcp.TestApp", Query("WinMcp.TestApp").List(new WindowFilter(TitleContains: "nope")).Hint);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_pid_is_an_invalid_argument(int pid)
    {
        var ex = Assert.Throws<WinMcpException>(() => Query("WinMcp.TestApp").List(new WindowFilter(Pid: pid)));

        Assert.Equal(WinMcpErrorCode.InvalidArgument, ex.Error.Code);
    }
}
