using System.Text.Json;
using ModelContextProtocol.Protocol;
using WinMcp.Core.Errors;
using WinMcp.Testing;

namespace WinMcp.Server.Tests;

public sealed class UiTreeToolTests
{
    private static (FakeDesktop Desktop, FakeUiAutomation Automation) Fakes()
    {
        var main = FakeDesktop.Window("WinMcp.TestApp", "WinMCP Test App", pid: 1, hwnd: 0x10);
        var automation = new FakeUiAutomation();
        automation.Trees[main.Hwnd] = FakeUiAutomation.TestAppTree();
        return (new FakeDesktop(main), automation);
    }

    private static async Task<InProcessServer> Start(FakeUiAutomation? automation = null)
    {
        var (desktop, defaultAutomation) = Fakes();
        return await InProcessServer.StartAsync(desktop, automation ?? defaultAutomation, "--allow", "WinMcp.TestApp");
    }

    [Fact]
    public async Task Get_ui_tree_returns_outline_text_and_json_structured_content()
    {
        await using var server = await Start();

        var result = await server.CallAsync("get_ui_tree", new() { ["hwnd"] = "hwnd:0x00000010" });

        Assert.NotEqual(true, result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.True(text.StartsWith("[e1] Window \"WinMCP Test App\" #MainForm", StringComparison.Ordinal), text);
        Assert.Contains("Edit \"Name:\" #nameTextBox value=\"\"", text);
        Assert.Contains("ComboBox \"Type:\" #typeComboBox value=\"Text\" collapsed", text);
        Assert.DoesNotContain("TitleBar", text);

        var json = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("hwnd:0x00000010", json.GetProperty("window").GetString());
        Assert.Equal(10, json.GetProperty("node_count").GetInt32());
        Assert.Equal("e1", json.GetProperty("root").GetProperty("ref").GetString());
    }

    [Fact]
    public async Task Get_ui_tree_declares_an_output_schema()
    {
        await using var server = await Start();

        var tools = await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        var schema = tools.Single(t => t.Name == "get_ui_tree").ProtocolTool.OutputSchema;
        Assert.NotNull(schema);
        Assert.True(schema.Value.GetProperty("properties").TryGetProperty("node_count", out _));
    }

    [Fact]
    public async Task Find_elements_returns_refs_that_drive_get_ui_tree()
    {
        await using var server = await Start();

        var found = await server.CallAsync("find_elements", new() { ["hwnd"] = "hwnd:0x00000010", ["control_type"] = "ComboBox" });
        var match = Assert.Single(Assert.IsType<JsonElement>(found.StructuredContent).GetProperty("matches").EnumerateArray());
        var comboRef = match.GetProperty("ref").GetString();

        var subtree = await server.CallAsync("get_ui_tree", new() { ["element"] = comboRef });

        Assert.NotEqual(true, subtree.IsError);
        Assert.StartsWith($"[{comboRef}] ComboBox", Assert.IsType<TextContentBlock>(Assert.Single(subtree.Content)).Text);
    }

    [Fact]
    public async Task Wrong_control_type_name_gets_a_correction_hint()
    {
        await using var server = await Start();

        var result = await server.CallAsync("find_elements", new() { ["hwnd"] = "hwnd:0x00000010", ["control_type"] = "TextBox" });

        Assert.True(result.IsError);
        var error = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("error");
        Assert.Equal("INVALID_ARGUMENT", error.GetProperty("code").GetString());
        Assert.Contains("'Edit'", error.GetProperty("hint").GetString());
    }

    [Fact]
    public async Task Unknown_ref_is_element_not_found()
    {
        await using var server = await Start();

        var result = await server.CallAsync("get_ui_tree", new() { ["element"] = "e404" });

        Assert.True(result.IsError);
        Assert.Equal("ELEMENT_NOT_FOUND", Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Hung_target_is_reported_as_retryable_environment_error()
    {
        var (_, automation) = Fakes();
        automation.ThrowOnFetch = new WinMcpException(new WinMcpError(WinMcpErrorCode.TargetNotResponding, "hung"));
        await using var server = await Start(automation);

        var result = await server.CallAsync("get_ui_tree", new() { ["hwnd"] = "hwnd:0x00000010" });

        Assert.True(result.IsError);
        var error = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("error");
        Assert.Equal("TARGET_NOT_RESPONDING", error.GetProperty("code").GetString());
        Assert.Equal("environment", error.GetProperty("category").GetString());
        Assert.True(error.GetProperty("retryable").GetBoolean());
    }
}
