using System.Text.Json;
using ModelContextProtocol.Protocol;
using WinMcp.Testing;

namespace WinMcp.Server.Tests;

public sealed class ScreenshotToolTests
{
    [Fact]
    public async Task Returns_a_png_image_block_and_json_metadata()
    {
        var desktop = new FakeDesktop(FakeDesktop.Window("WinMcp.TestApp", "WinMCP Test App", pid: 1, hwnd: 0x10));
        await using var server = await InProcessServer.StartAsync(desktop, "--allow", "WinMcp.TestApp");

        var result = await server.CallAsync("capture_screenshot", new() { ["hwnd"] = "hwnd:0x00000010", ["max_edge"] = 800 });

        Assert.NotEqual(true, result.IsError);
        var image = Assert.IsType<ImageContentBlock>(result.Content[0]);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(FakeScreenCapture.Png, image.DecodedData.ToArray());
        var info = JsonDocument.Parse(Assert.IsType<TextContentBlock>(result.Content[1]).Text).RootElement;
        Assert.Equal("hwnd:0x00000010", info.GetProperty("window").GetString());
        Assert.Equal(300, info.GetProperty("source_bounds").GetProperty("width").GetInt32());
        Assert.Equal(800, Assert.Single(server.Capture.Requests).MaxEdge);
    }

    [Fact]
    public async Task Is_a_read_only_tool_available_in_observe_mode()
    {
        await using var server = await InProcessServer.StartAsync(new FakeDesktop());

        var tool = (await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).Single(t => t.Name == "capture_screenshot");

        Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint);
    }
}
