using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace WinMcp.E2ETests;

public sealed class ListWindowsE2ETests
{
    [Fact]
    [Trait("Category", "Windows")]
    public async Task Discovers_the_running_TestApp_through_MCP()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var testApp = Process.Start(E2E.TestAppPath, "--position 200,200");
        try
        {
            var (client, _) = await E2E.StartServerAsync("--allow", "WinMcp.TestApp");
            await using var _ = client;

            // The app needs a moment to create its window; poll through the tool rather than sleeping blindly.
            JsonElement window = default;
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
            {
                var result = await client.CallToolAsync("list_windows", new Dictionary<string, object?> { ["pid"] = testApp.Id }, cancellationToken: cancellationToken);
                Assert.NotEqual(true, result.IsError);
                var windows = Structured(result).GetProperty("windows");
                if (windows.GetArrayLength() == 1)
                {
                    window = windows[0];
                    break;
                }
                await Task.Delay(100, cancellationToken);
            }

            Assert.Equal(JsonValueKind.Object, window.ValueKind);
            Assert.Equal("WinMCP Test App", window.GetProperty("title").GetString());
            Assert.Equal(testApp.Id, window.GetProperty("process").GetProperty("pid").GetInt32());
            Assert.StartsWith("hwnd:0x", window.GetProperty("hwnd").GetString());

            // The agent's natural next step: inspect the window it just found, by handle.
            var inspection = await client.CallToolAsync(
                "inspect_window",
                new Dictionary<string, object?> { ["hwnd"] = window.GetProperty("hwnd").GetString() },
                cancellationToken: cancellationToken);
            Assert.NotEqual(true, inspection.IsError);
            var details = Structured(inspection);
            Assert.True(details.GetProperty("responding").GetBoolean());
            Assert.Equal("winforms", details.GetProperty("framework_hint").GetString());
            Assert.True(details.GetProperty("child_windows").GetProperty("count").GetInt32() >= 12);
        }
        finally
        {
            testApp.Kill();
        }
    }

    [Fact]
    public async Task Without_an_allowlist_nothing_is_visible_and_the_hint_says_why()
    {
        var (client, _) = await E2E.StartServerAsync();
        await using var _ = client;

        var result = await client.CallToolAsync("list_windows", cancellationToken: TestContext.Current.CancellationToken);

        var content = Structured(result);
        Assert.Equal(0, content.GetProperty("windows").GetArrayLength());
        Assert.Contains("--allow", content.GetProperty("hint").GetString());
    }

    private static JsonElement Structured(CallToolResult result) =>
        Assert.IsType<JsonElement>(result.StructuredContent);
}
