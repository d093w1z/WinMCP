using System.Diagnostics;
using WinMcp.Core.Errors;
using WinMcp.Windows.Automation;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>No GUI needed: exercises the dispatcher's thread handling with synthetic work.</summary>
public sealed class AutomationDispatcherTests
{
    [Fact]
    public async Task Runs_work_on_a_dedicated_MTA_thread()
    {
        using var dispatcher = new AutomationDispatcher(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));

        var (apartment, name) = await dispatcher.RunAsync(
            _ => (Thread.CurrentThread.GetApartmentState(), Thread.CurrentThread.Name),
            TestContext.Current.CancellationToken);

        Assert.Equal(ApartmentState.MTA, apartment);
        Assert.Equal("WinMCP UI Automation", name);
    }

    [Fact]
    public async Task Stuck_work_is_abandoned_after_the_hard_timeout_and_a_fresh_thread_serves_the_next_call()
    {
        var token = TestContext.Current.CancellationToken;
        using var dispatcher = new AutomationDispatcher(TimeSpan.FromSeconds(3), hardTimeout: TimeSpan.FromMilliseconds(500));
        var release = new ManualResetEventSlim();
        var stuckThread = 0;

        var stopwatch = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => dispatcher.RunAsync(_ =>
        {
            stuckThread = Environment.CurrentManagedThreadId;
            release.Wait(TimeSpan.FromSeconds(30));
            return 0;
        }, token));
        Assert.Equal(WinMcpErrorCode.TargetNotResponding, ex.Error.Code);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));

        var nextThread = await dispatcher.RunAsync(_ => Environment.CurrentManagedThreadId, token);
        Assert.NotEqual(stuckThread, nextThread);
        release.Set();
    }

    [Fact]
    public async Task Exceptions_from_work_propagate_unchanged()
    {
        using var dispatcher = new AutomationDispatcher(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.RunAsync<int>(_ => throw new InvalidOperationException("boom"), TestContext.Current.CancellationToken));
    }
}
