using System.Text.Json;
using WinMcp.Core.Automation;
using WinMcp.Testing;

namespace WinMcp.Server.Tests;

public sealed class ControlToolTests : IDisposable
{
    private static readonly string[] ControlTools = ["invoke", "select_option", "send_keys", "set_toggle", "set_value"];
    private readonly string _auditDirectory = Path.Combine(Path.GetTempPath(), $"winmcp-audit-{Guid.NewGuid():N}");
    private readonly FakeUiAutomation _automation = new();

    public ControlToolTests() => _automation.Trees[new(0x10)] = FakeUiAutomation.TestAppTree();

    public void Dispose()
    {
        if (Directory.Exists(_auditDirectory)) Directory.Delete(_auditDirectory, recursive: true);
    }

    private Task<InProcessServer> Start(string mode) =>
        InProcessServer.StartAsync(
            new FakeDesktop(FakeDesktop.Window("WinMcp.TestApp", "WinMCP Test App", pid: 1, hwnd: 0x10)),
            _automation,
            "--mode", mode, "--allow", "WinMcp.TestApp", "--audit-dir", _auditDirectory);

    [Fact]
    public async Task Observe_mode_does_not_list_control_tools_but_offers_wait_for()
    {
        await using var server = await Start("observe");

        var names = (await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).Select(t => t.Name).ToList();

        Assert.DoesNotContain(names, ControlTools.Contains);
        Assert.Contains("wait_for", names);
    }

    [Fact]
    public async Task Control_mode_lists_control_tools_with_honest_annotations()
    {
        await using var server = await Start("control");

        var tools = (await server.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).ToDictionary(t => t.Name, t => t.ProtocolTool.Annotations);

        Assert.All(ControlTools, name => Assert.False(tools[name]?.ReadOnlyHint));
        Assert.True(tools["invoke"]?.DestructiveHint);   // a click can do anything, e.g. "Delete all"
        Assert.False(tools["invoke"]?.IdempotentHint);
        Assert.True(tools["set_toggle"]?.IdempotentHint); // target state, not a flip
        Assert.True(tools["send_keys"]?.DestructiveHint); // shortcuts can do anything
        Assert.Contains("interact", server.Client.ServerInstructions);
    }

    [Fact]
    public async Task Set_value_goes_through_and_is_audited()
    {
        _automation.OnPerform = (_, _) => new ActionOutcome("uia.ValuePattern", true, ValueAfter: "Mukesh");
        await using var server = await Start("control");

        var result = await server.CallAsync("set_value", new() { ["hwnd"] = "hwnd:0x00000010", ["automation_id"] = "nameTextBox", ["value"] = "Mukesh" });

        Assert.NotEqual(true, result.IsError);
        var content = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("uia.ValuePattern", content.GetProperty("method").GetString());
        Assert.Equal("Mukesh", content.GetProperty("value_after").GetString());

        var line = Assert.Single(File.ReadAllLines(Assert.Single(Directory.GetFiles(_auditDirectory, "audit-*.jsonl"))));
        Assert.Contains("\"tool\":\"set_value\"", line);
        Assert.Contains("\"outcome\":\"ok\"", line);
    }

    [Fact]
    public async Task Disabled_button_is_refused_as_a_caller_error()
    {
        await using var server = await Start("control");

        var result = await server.CallAsync("invoke", new() { ["hwnd"] = "hwnd:0x00000010", ["automation_id"] = "advancedButton" });

        Assert.True(result.IsError);
        var error = Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("error");
        Assert.Equal("ELEMENT_DISABLED", error.GetProperty("code").GetString());
        Assert.Empty(_automation.Performed);
    }

    [Theory]
    [InlineData("set_toggle", "state", "maybe")]
    [InlineData("wait_for", "condition", "eventually")]
    public async Task Unknown_enum_values_are_invalid_arguments(string tool, string argument, string value)
    {
        await using var server = await Start("control");

        var result = await server.CallAsync(tool, new() { ["hwnd"] = "hwnd:0x00000010", ["automation_id"] = "enableCheckBox", [argument] = value });

        Assert.True(result.IsError);
        Assert.Equal("INVALID_ARGUMENT", Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("error").GetProperty("code").GetString());
    }
}
