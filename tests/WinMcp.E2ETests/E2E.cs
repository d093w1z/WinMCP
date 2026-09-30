using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using ModelContextProtocol.Client;

namespace WinMcp.E2ETests;

/// <summary>Locates the built executables and starts the real server over stdio.</summary>
internal static class E2E
{
    public static string ServerPath => Resolve("WINMCP_SERVER_PATH", "ServerPath");

    public static string TestAppPath => Resolve("WINMCP_TESTAPP_PATH", "TestAppPath");

    /// <summary>Starts the TestApp inside the test job, so it can't outlive an interrupted run.</summary>
    public static Process StartTestApp()
    {
        var process = Process.Start(TestAppPath, "--position 200,200");
        WinMcp.Testing.ChildProcessJob.Add(process.Id);
        return process;
    }

    public static async Task<(McpClient Client, ConcurrentQueue<string> Stderr)> StartServerAsync(params string[] args)
    {
        var stderr = new ConcurrentQueue<string>();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "WinMCP",
            Command = ServerPath,
            Arguments = args,
            StandardErrorLines = stderr.Enqueue,
            // The SDK waits this long for the server to exit before closing its stdin; the server itself exits
            // ~30 ms after stdin closes (measured in M8), so the 5 s default only slowed every test down.
            ShutdownTimeout = TimeSpan.FromMilliseconds(500),
        });
        var client = await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
        return (client, stderr);
    }

    private static string Resolve(string environmentVariable, string metadataKey) =>
        Path.GetFullPath(
            Environment.GetEnvironmentVariable(environmentVariable) is { Length: > 0 } overridePath
                ? overridePath
                : typeof(E2E).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == metadataKey).Value!);
}
