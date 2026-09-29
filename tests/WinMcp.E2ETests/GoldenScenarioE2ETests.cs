using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace WinMcp.E2ETests;

/// <summary>
/// The MVP scenario (plan §H.3) exactly as an agent would run it: real server exe, stdio, control mode, real TestApp.
/// </summary>
public sealed class GoldenScenarioE2ETests : IDisposable
{
    private const string GoldenStatus = "Status: Applied: Name=Mukesh; Type=HTML; Feature=On";
    private readonly string _auditDirectory = Path.Combine(Path.GetTempPath(), $"winmcp-e2e-audit-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_auditDirectory)) Directory.Delete(_auditDirectory, recursive: true);
    }

    [Fact]
    [Trait("Category", "Windows")]
    public async Task Enter_name_select_type_apply_and_read_status_through_MCP()
    {
        var token = TestContext.Current.CancellationToken;
        using var testApp = E2E.StartTestApp();
        var (client, _) = await E2E.StartServerAsync("--mode", "control", "--allow", "WinMcp.TestApp", "--audit-dir", _auditDirectory);
        await using var server = client;

        var hwnd = await WaitForWindowAsync(client, testApp.Id, token);

        // Discover controls the way an agent would: by what they are, not by where they are.
        var name = await Ref(client, hwnd, new() { ["control_type"] = "Edit", ["name"] = "Name:" }, token);
        var type = await Ref(client, hwnd, new() { ["control_type"] = "ComboBox" }, token);
        var feature = await Ref(client, hwnd, new() { ["control_type"] = "CheckBox" }, token);
        var apply = await Ref(client, hwnd, new() { ["control_type"] = "Button", ["name"] = "Apply" }, token);

        await Call(client, "set_value", new() { ["element"] = name, ["value"] = "Mukesh" }, token);
        await Call(client, "select_option", new() { ["element"] = type, ["option"] = "HTML" }, token);
        await Call(client, "set_toggle", new() { ["element"] = feature, ["state"] = "on" }, token);
        await Call(client, "invoke", new() { ["element"] = apply }, token);
        var status = await Call(client, "wait_for", new()
        {
            ["hwnd"] = hwnd, ["automation_id"] = "statusLabel", ["condition"] = "text_equals", ["text"] = GoldenStatus,
        }, token);

        Assert.Equal(GoldenStatus, status.GetProperty("element").GetProperty("name").GetString());

        // MVP criterion: only semantic methods, never coordinates.
        var entries = Directory.GetFiles(_auditDirectory, "audit-*.jsonl").SelectMany(File.ReadAllLines)
            .Select(line => JsonDocument.Parse(line).RootElement).ToList();
        Assert.Equal(["set_value", "select_option", "set_toggle", "invoke"], entries.Select(e => e.GetProperty("tool").GetString()));
        Assert.All(entries, e => Assert.Equal("ok", e.GetProperty("outcome").GetString()));
        Assert.All(entries.Where(e => e.TryGetProperty("method", out _)), e =>
            Assert.Matches("^(uia|win32)\\.|^none$", e.GetProperty("method").GetString()));
    }

    private static async Task<string> Ref(McpClient client, string hwnd, Dictionary<string, object?> criteria, CancellationToken token)
    {
        criteria["hwnd"] = hwnd;
        var found = await Call(client, "find_elements", criteria, token);
        return Assert.Single(found.GetProperty("matches").EnumerateArray()).GetProperty("ref").GetString()!;
    }

    private static async Task<JsonElement> Call(McpClient client, string tool, Dictionary<string, object?> arguments, CancellationToken token)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: token);
        var content = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.True(result.IsError != true, $"{tool} failed: {content}");
        return content;
    }

    private static async Task<string> WaitForWindowAsync(McpClient client, int pid, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
        {
            var windows = (await Call(client, "list_windows", new() { ["pid"] = pid }, token)).GetProperty("windows");
            if (windows.GetArrayLength() == 1)
                return windows[0].GetProperty("hwnd").GetString()!;
            await Task.Delay(100, token);
        }
        throw new TimeoutException("TestApp window did not appear.");
    }
}
