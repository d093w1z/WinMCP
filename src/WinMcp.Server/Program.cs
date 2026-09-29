using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinMcp.Core.Desktop;
using WinMcp.Core.Policy;
using WinMcp.Server;
using WinMcp.Windows;

if (args is ["--help"] or ["-h"])
{
    Console.Error.WriteLine(WinMcpOptions.Usage);
    return 0;
}

WinMcpOptions options;
try
{
    options = WinMcpOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"winmcp: {ex.Message}");
    Console.Error.WriteLine(WinMcpOptions.Usage);
    return 2;
}

// Arguments are WinMCP's own; don't let the host reinterpret them as configuration.
var builder = Host.CreateApplicationBuilder();

// stdout carries JSON-RPC. Anything else written there corrupts the protocol, so all logging goes to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<IDesktop, Win32Desktop>();
builder.Services.AddWinMcpServer(options).WithStdioServerTransport();

await builder.Build().RunAsync();
return 0;
