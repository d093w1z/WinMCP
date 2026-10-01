using System.Text.Json;
using WinMcp.Testing;

namespace WinMcp.Server.Tests;

public sealed class InspectWindowToolTests
{
    private static FakeDesktop Desktop()
    {
        var main = FakeDesktop.Window("WinMcp.TestApp", "WinMCP Test App", pid: 1, hwnd: 0xA0B1C, className: "WindowsForms10.Window.8.app.0.1");
        var desktop = new FakeDesktop(
            main,
            FakeDesktop.Window("WinMcp.TestApp", "Options", pid: 1, hwnd: 0xA0B1D, owner: 0xA0B1C),
            FakeDesktop.Window("mail", "Password reset for bob", pid: 2, hwnd: 0x20));
        desktop.Details[main.Hwnd] = FakeDesktop.DefaultDetails(main, "WindowsForms10.BUTTON.app.0.1", "WindowsForms10.EDIT.app.0.1");
        return desktop;
    }

    [Fact]
    public async Task Returns_structured_window_inspection()
    {
        await using var server = await InProcessServer.StartAsync(Desktop(), "--allow", "WinMcp.TestApp");

        var result = await server.CallAsync("inspect_window", new() { ["hwnd"] = "hwnd:0x000A0B1C" });

        Assert.NotEqual(true, result.IsError);
        var content = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("WinMCP Test App", content.GetProperty("window").GetProperty("title").GetString());
        Assert.True(content.GetProperty("responding").GetBoolean());
        Assert.Equal("winforms", content.GetProperty("framework_hint").GetString());
        Assert.Contains("WS_CAPTION", content.GetProperty("styles").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal("Options", content.GetProperty("owned_windows")[0].GetProperty("title").GetString());
        Assert.Equal(2, content.GetProperty("child_windows").GetProperty("count").GetInt32());
        // Class names are data, not property names: the snake_case policy must not rewrite dictionary keys.
        Assert.True(content.GetProperty("child_windows").GetProperty("by_class").TryGetProperty("WindowsForms10.BUTTON.app.0.1", out _));
    }

    [Fact]
    public async Task Non_allowlisted_window_reports_not_found_without_details()
    {
        await using var server = await InProcessServer.StartAsync(Desktop(), "--allow", "WinMcp.TestApp");

        var result = await server.CallAsync("inspect_window", new() { ["hwnd"] = "hwnd:0x00000020" });

        Assert.True(result.IsError);
        Assert.Equal("WINDOW_NOT_FOUND", Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("bob", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Malformed_handle_is_an_invalid_argument()
    {
        await using var server = await InProcessServer.StartAsync(Desktop(), "--allow", "WinMcp.TestApp");

        var result = await server.CallAsync("inspect_window", new() { ["hwnd"] = "12345" });

        Assert.True(result.IsError);
        Assert.Equal("INVALID_ARGUMENT", Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("error").GetProperty("code").GetString());
    }
}
