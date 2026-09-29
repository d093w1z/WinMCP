using System.Diagnostics;
using System.Runtime.InteropServices;
using WinMcp.Core.Errors;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>Plan §A.3: a hung target produces TARGET_NOT_RESPONDING quickly and WinMCP recovers afterwards.</summary>
[Trait("Category", "Windows")]
public sealed partial class HungTargetTests : IClassFixture<TestAppSession>, IDisposable
{
    private const uint BmClick = 0x00F5;
    private readonly TestAppSession _app;
    private readonly WinMcpServices _services = new();

    public HungTargetTests(TestAppSession app)
    {
        _app = app;
        _app.Reset();
    }

    public void Dispose() => _services.Dispose();

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);

    [Fact]
    public async Task Hung_window_times_out_fast_is_reported_as_not_responding_and_recovers()
    {
        var token = TestContext.Current.CancellationToken;
        var hwnd = _app.WindowHandle.ToString();

        // Posted, not invoked: a UIA Invoke would itself block on the frozen UI thread.
        var freezeButton = _app.Find("freezeButton").Properties.NativeWindowHandle.Value;
        Assert.True(PostMessage(freezeButton, BmClick, 0, 0));
        var frozenAt = Stopwatch.StartNew();
        await Task.Delay(300, token); // let the app pick up the click and start blocking

        var timing = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<WinMcpException>(() => _services.Tree.GetTreeAsync(hwnd, null, 10, 300, token));
        Assert.Equal(WinMcpErrorCode.TargetNotResponding, ex.Error.Code);
        Assert.True(timing.Elapsed < TimeSpan.FromSeconds(5), $"timed out after {timing.Elapsed.TotalSeconds:F1} s; expected ~3 s UIA timeout");

        // Windows flags the window as hung after ~5 s without message processing; the freeze lasts 8 s.
        var sawNotResponding = false;
        while (frozenAt.Elapsed < TimeSpan.FromSeconds(8) && !sawNotResponding)
        {
            sawNotResponding = !_services.Windows.Inspect(hwnd).Responding;
            await Task.Delay(200, token);
        }
        Assert.True(sawNotResponding, "inspect_window never reported responding=false during the freeze");

        // Recovery: the same services work again once the app unfreezes.
        TestAppSession.WaitUntil(() => _services.Windows.Inspect(hwnd).Responding, "the app responds again", TimeSpan.FromSeconds(10));
        var tree = await _services.Tree.GetTreeAsync(hwnd, null, 10, 300, token);
        Assert.Equal("Window", tree.Root.ControlType);
    }
}
