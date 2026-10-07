using WinMcp.Core.Desktop;
using WinMcp.Core.Policy;

namespace WinMcp.Core.Tests.Policy;

public sealed class TargetPolicyTests
{
    private const int OwnPid = 4242;

    private static TargetPolicy Policy(params string[] allow) => new(new WinMcpOptions(ServerMode.Observe, allow), OwnPid);

    private static ProcessInfo Process(string name, string? path = null, int pid = 100) =>
        new(pid, name, path ?? $@"C:\Apps\{name}.exe", "x64", false);

    [Theory]
    [InlineData("WinMcp.TestApp")]
    [InlineData("winmcp.testapp")]
    [InlineData("WinMcp.TestApp.exe")]
    public void Name_entries_match_case_insensitively_with_optional_exe(string entry) =>
        Assert.True(Policy(entry).IsAllowed(Process("WinMcp.TestApp")));

    [Fact]
    public void Path_entries_match_only_that_executable()
    {
        var policy = Policy(@"C:\Apps\Tool.exe");

        Assert.True(policy.IsAllowed(Process("Tool", @"c:\apps\TOOL.EXE")));
        Assert.False(policy.IsAllowed(Process("Tool", @"C:\Elsewhere\Tool.exe")));
    }

    [Fact]
    public void Empty_allowlist_allows_nothing() =>
        Assert.False(Policy().IsAllowed(Process("notepad")));

    [Fact]
    public void Unlisted_process_is_not_allowed() =>
        Assert.False(Policy("WinMcp.TestApp").IsAllowed(Process("notepad")));

    [Theory]
    [InlineData("consent")]
    [InlineData("LogonUI")]
    [InlineData("winlogon")]
    [InlineData("CredentialUIBroker")]
    public void Denied_system_ui_is_never_allowed_even_when_listed(string name) =>
        Assert.False(Policy(name).IsAllowed(Process(name)));

    [Fact]
    public void WinMcp_itself_is_never_allowed() =>
        Assert.False(Policy("WinMcp.Server").IsAllowed(Process("WinMcp.Server", pid: OwnPid)));

    [Fact]
    public void Allow_entries_for_deny_listed_processes_are_reported_as_ineffective() =>
        Assert.Equal(["consent.exe", @"C:\Windows\System32\LogonUI.exe"],
            Policy("WinMcp.TestApp", "consent.exe", @"C:\Windows\System32\LogonUI.exe").IneffectiveAllowEntries);
}
