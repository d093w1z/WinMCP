using System.IO.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;
using WinMcp.Core.Policy;
using WinMcp.Testing;

namespace WinMcp.Server.Tests;

/// <summary>The production server setup, hosted in-process and connected to a real MCP client over in-memory pipes.</summary>
internal sealed class InProcessServer : IAsyncDisposable
{
    private readonly IHost _host;

    private InProcessServer(IHost host, McpClient client)
    {
        _host = host;
        Client = client;
    }

    public McpClient Client { get; }

    /// <summary>This server's screen capture fake; <see cref="FakeScreenCapture.Requests"/> shows what was captured.</summary>
    public FakeScreenCapture Capture { get; private init; } = new();

    public static Task<InProcessServer> StartAsync(IDesktop desktop, params string[] args) =>
        StartAsync(desktop, new FakeUiAutomation(), args);

    public static async Task<InProcessServer> StartAsync(IDesktop desktop, IUiAutomation automation, params string[] args)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(desktop);
        builder.Services.AddSingleton(automation);
        var capture = new FakeScreenCapture();
        builder.Services.AddSingleton<WinMcp.Core.Capture.IScreenCapture>(capture);
        builder.Services
            .AddWinMcpServer(WinMcpOptions.Parse(args))
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream());

        var host = builder.Build();
        await host.StartAsync(cancellationToken);

        var client = await McpClient.CreateAsync(
            new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream()),
            cancellationToken: cancellationToken);
        return new InProcessServer(host, client) { Capture = capture };
    }

    public ValueTask<CallToolResult> CallAsync(string tool, Dictionary<string, object?>? arguments = null) =>
        Client.CallToolAsync(tool, arguments ?? [], cancellationToken: TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _host.StopAsync();
        _host.Dispose();
    }
}
