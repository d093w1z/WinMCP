using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace WinMcp.E2ETests;

public sealed class UiTreeE2ETests
{
    [Fact]
    [Trait("Category", "Windows")]
    public async Task Agent_style_read_flow_finds_the_status_through_MCP()
    {
        var token = TestContext.Current.CancellationToken;
        using var testApp = Process.Start(E2E.TestAppPath, "--position 200,200");
        try
        {
            var (client, _) = await E2E.StartServerAsync("--allow", "WinMcp.TestApp");
            await using var _ = client;
            var hwnd = await WaitForWindowAsync(client, testApp.Id, token);

            var tree = await client.CallToolAsync("get_ui_tree", new Dictionary<string, object?> { ["hwnd"] = hwnd }, cancellationToken: token);
            Assert.NotEqual(true, tree.IsError);
            var outline = Assert.IsType<TextContentBlock>(Assert.Single(tree.Content)).Text;
            Assert.Contains("Edit \"Name:\" #nameTextBox", outline);
            Assert.Contains("Button \"Apply\" #applyButton", outline);

            var found = await client.CallToolAsync(
                "find_elements",
                new Dictionary<string, object?> { ["hwnd"] = hwnd, ["automation_id"] = "statusLabel" },
                cancellationToken: token);
            var status = Assert.Single(Assert.IsType<JsonElement>(found.StructuredContent).GetProperty("matches").EnumerateArray());
            Assert.Equal("Status: Ready", status.GetProperty("name").GetString());
            Assert.Contains($"[{status.GetProperty("ref").GetString()}] Text \"Status: Ready\"", outline); // same ref in both tools
        }
        finally
        {
            testApp.Kill();
        }
    }

    private static async Task<string> WaitForWindowAsync(McpClient client, int pid, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
        {
            var result = await client.CallToolAsync("list_windows", new Dictionary<string, object?> { ["pid"] = pid }, cancellationToken: token);
            var windows = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("windows");
            if (windows.GetArrayLength() == 1)
                return windows[0].GetProperty("hwnd").GetString()!;
            await Task.Delay(100, token);
        }
        throw new TimeoutException("TestApp window did not appear.");
    }
}
