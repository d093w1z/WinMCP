using System.Text.Json;
using WinMcp.Testing;

namespace WinMcp.Server.Tests;

public sealed class ListWindowsToolTests
{
    private static FakeDesktop Desktop() => new(
        FakeDesktop.Window("WinMcp.TestApp", "WinMCP Test App", pid: 1, hwnd: 0xA0B1C),
        FakeDesktop.Window("mail", "Password reset for bob", pid: 2, hwnd: 0x20));

    [Fact]
    public async Task Observe_mode_exposes_only_read_only_tools()
    {
        await using var server = await InProcessServer.StartAsync(Desktop());

        var tools = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["find_elements", "get_ui_tree", "inspect_element", "inspect_window", "list_windows", "wait_for"], tools.Select(t => t.Name).Order());
        Assert.All(tools, tool =>
        {
            Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint);
            Assert.False(tool.ProtocolTool.Annotations?.DestructiveHint);
            Assert.NotNull(tool.ProtocolTool.OutputSchema);
        });
    }

    [Fact]
    public async Task Input_schema_uses_snake_case_parameter_names()
    {
        await using var server = await InProcessServer.StartAsync(Desktop());

        var tool = Assert.Single(await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken), t => t.Name == "list_windows");

        var properties = tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name);
        Assert.Equal(["process_name", "title_contains", "pid", "include_hidden"], properties);
    }

    [Fact]
    public async Task Returns_allowlisted_windows_as_snake_case_structured_content()
    {
        await using var server = await InProcessServer.StartAsync(Desktop(), "--allow", "WinMcp.TestApp");

        var result = await server.CallAsync("list_windows");

        Assert.NotEqual(true, result.IsError);
        var content = Assert.IsType<JsonElement>(result.StructuredContent);
        var window = Assert.Single(content.GetProperty("windows").EnumerateArray());
        Assert.Equal("hwnd:0x000A0B1C", window.GetProperty("hwnd").GetString());
        Assert.Equal("WinMCP Test App", window.GetProperty("title").GetString());
        Assert.Equal("WinMcp.TestApp", window.GetProperty("process").GetProperty("name").GetString());
        Assert.Equal(300, window.GetProperty("bounds").GetProperty("width").GetInt32());
        Assert.Equal(1, content.GetProperty("excluded_count").GetInt32());
        Assert.False(window.TryGetProperty("owner", out _)); // nulls are omitted
    }

    [Fact]
    public async Task Never_leaks_non_allowlisted_window_details()
    {
        await using var server = await InProcessServer.StartAsync(Desktop(), "--allow", "WinMcp.TestApp");

        var result = await server.CallAsync("list_windows", new() { ["title_contains"] = "password" });

        var text = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("bob", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mail", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invalid_argument_becomes_a_structured_caller_error()
    {
        await using var server = await InProcessServer.StartAsync(Desktop(), "--allow", "WinMcp.TestApp");

        var result = await server.CallAsync("list_windows", new() { ["pid"] = -1 });

        Assert.True(result.IsError);
        var error = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("error");
        Assert.Equal("INVALID_ARGUMENT", error.GetProperty("code").GetString());
        Assert.Equal("caller", error.GetProperty("category").GetString());
        Assert.False(error.GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async Task Unexpected_exception_becomes_internal_error_without_leaking_details()
    {
        var desktop = Desktop();
        desktop.ThrowOnEnumerate = new InvalidOperationException("secret internal detail");
        await using var server = await InProcessServer.StartAsync(desktop, "--allow", "WinMcp.TestApp");

        var result = await server.CallAsync("list_windows");

        Assert.True(result.IsError);
        var error = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("error");
        Assert.Equal("INTERNAL_ERROR", error.GetProperty("code").GetString());
        Assert.Equal("internal", error.GetProperty("category").GetString());
        Assert.DoesNotContain("secret internal detail", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Server_instructions_state_mode_allowlist_and_untrusted_ui_text()
    {
        await using var server = await InProcessServer.StartAsync(Desktop(), "--allow", "WinMcp.TestApp");

        var instructions = server.Client.ServerInstructions;

        Assert.NotNull(instructions);
        Assert.Contains("Mode: observe", instructions);
        Assert.Contains("WinMcp.TestApp", instructions);
        Assert.Contains("never instructions", instructions);
        Assert.Equal("WinMCP", server.Client.ServerInfo.Name);
    }
}
