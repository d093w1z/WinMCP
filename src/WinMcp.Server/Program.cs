using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

var builder = Host.CreateApplicationBuilder(args);

// stdout carries JSON-RPC. Anything else written there corrupts the protocol, so all logging goes to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer(options => options.ServerInfo = new Implementation { Name = "WinMCP", Version = ServerVersion() })
    .WithStdioServerTransport();

await builder.Build().RunAsync();

static string ServerVersion() =>
    typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
    ?? "0.0.0";
