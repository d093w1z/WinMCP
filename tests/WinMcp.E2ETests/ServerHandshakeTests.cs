using System.Diagnostics;
using System.Reflection;

namespace WinMcp.E2ETests;

public sealed class ServerHandshakeTests
{
    [Fact]
    public async Task Server_completes_the_MCP_handshake_over_stdio_and_logs_only_to_stderr()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // If anything but JSON-RPC reached stdout, the handshake itself would fail here.
        var (client, stderr) = await E2E.StartServerAsync();
        await using var _ = client;

        Assert.Equal("WinMCP", client.ServerInfo.Name);
        // Same Directory.Build.props version as this test assembly.
        Assert.Equal(typeof(ServerHandshakeTests).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0], client.ServerInfo.Version);

        var stopwatch = Stopwatch.StartNew();
        while (!stderr.Any(line => line.Contains("Application started")) && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(50, cancellationToken);
        Assert.Contains(stderr, line => line.Contains("Application started"));
    }

    [Theory]
    [InlineData("--mode", "control")]
    [InlineData("--mode", "1")]
    [InlineData("--bogus")]
    public async Task Invalid_arguments_exit_with_code_2_and_usage_on_stderr(params string[] args)
    {
        using var process = Process.Start(new ProcessStartInfo(E2E.ServerPath, args)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var stderr = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stdout = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, process.ExitCode);
        Assert.Contains("Usage: WinMcp.Server", stderr);
        Assert.Empty(stdout);
    }
}
