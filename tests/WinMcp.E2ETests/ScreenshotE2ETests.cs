using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace WinMcp.E2ETests;

public sealed class ScreenshotE2ETests
{
    [Fact]
    [Trait("Category", "Windows")]
    public async Task Capture_screenshot_returns_a_png_image_block_over_stdio()
    {
        var token = TestContext.Current.CancellationToken;
        using var testApp = E2E.StartTestApp();
        var (client, _) = await E2E.StartServerAsync("--allow", "WinMcp.TestApp");
        await using var server = client;

        string? hwnd = null;
        var stopwatch = Stopwatch.StartNew();
        while (hwnd is null && stopwatch.Elapsed < TimeSpan.FromSeconds(10))
        {
            var list = await client.CallToolAsync("list_windows", new Dictionary<string, object?> { ["pid"] = testApp.Id }, cancellationToken: token);
            var windows = Assert.IsType<JsonElement>(list.StructuredContent).GetProperty("windows");
            hwnd = windows.GetArrayLength() == 1 ? windows[0].GetProperty("hwnd").GetString() : null;
            if (hwnd is null) await Task.Delay(100, token);
        }
        Assert.NotNull(hwnd);

        var result = await client.CallToolAsync("capture_screenshot", new Dictionary<string, object?> { ["hwnd"] = hwnd }, cancellationToken: token);

        Assert.NotEqual(true, result.IsError);
        var image = Assert.IsType<ImageContentBlock>(result.Content[0]);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], image.DecodedData.ToArray().Take(4));
        var info = JsonDocument.Parse(Assert.IsType<TextContentBlock>(result.Content[1]).Text).RootElement;
        Assert.Equal("PrintWindow", info.GetProperty("method").GetString());
        Assert.True(info.GetProperty("image_width").GetInt32() > 100);
    }
}
