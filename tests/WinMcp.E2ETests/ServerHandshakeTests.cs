using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using ModelContextProtocol.Client;

namespace WinMcp.E2ETests;

public sealed class ServerHandshakeTests
{
    private static string ServerPath =>
        Environment.GetEnvironmentVariable("WINMCP_SERVER_PATH") is { Length: > 0 } overridePath
            ? overridePath
            : typeof(ServerHandshakeTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(a => a.Key == "ServerPath").Value!;

    [Fact]
    public async Task Server_completes_the_MCP_handshake_over_stdio_and_logs_only_to_stderr()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stderr = new ConcurrentQueue<string>();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "WinMCP",
            Command = Path.GetFullPath(ServerPath),
            StandardErrorLines = stderr.Enqueue,
        });

        // If anything but JSON-RPC reached stdout, the handshake itself would fail here.
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

        Assert.Equal("WinMCP", client.ServerInfo.Name);
        Assert.Equal("0.1.0", client.ServerInfo.Version);

        var stopwatch = Stopwatch.StartNew();
        while (!stderr.Any(line => line.Contains("Application started")) && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(50, cancellationToken);
        Assert.Contains(stderr, line => line.Contains("Application started"));
    }
}
