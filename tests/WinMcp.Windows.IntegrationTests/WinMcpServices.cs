using WinMcp.Core.Audit;
using WinMcp.Core.Automation;
using WinMcp.Core.Desktop;
using WinMcp.Core.Policy;
using WinMcp.Core.Symbols;
using WinMcp.Windows.Automation;

namespace WinMcp.Windows.IntegrationTests;

/// <summary>The production Core + Windows object graph, wired as the server wires it (default: allowlisting only the TestApp).</summary>
internal sealed class WinMcpServices : IDisposable
{
    public WinMcpServices(params string[] serverArguments)
    {
        var options = WinMcpOptions.Parse(serverArguments.Length > 0 ? serverArguments : ["--allow", "WinMcp.TestApp"]);
        Dispatcher = new AutomationDispatcher(uiaTimeout: TimeSpan.FromSeconds(3), hardTimeout: TimeSpan.FromSeconds(10));
        Automation = new UiaAutomation(Dispatcher);
        Windows = new WindowQuery(new Win32Desktop(), new TargetPolicy(options, Environment.ProcessId));
        Tree = new UiTreeService(Windows, Automation, new ElementRegistry(), new SymbolProvider(options));
        Interaction = new InteractionService(Tree, Automation, new Win32Keyboard(), Windows, options, new JsonlAuditLog(options.AuditDirectory));
        AuditDirectory = options.AuditDirectory;
    }

    /// <summary>Control-mode services allowlisting the TestApp, auditing to a fresh temp directory.</summary>
    public static WinMcpServices Control() =>
        new("--mode", "control", "--allow", "WinMcp.TestApp", "--audit-dir", Path.Combine(Path.GetTempPath(), $"winmcp-audit-{Guid.NewGuid():N}"));

    public InteractionService Interaction { get; }

    public string AuditDirectory { get; }

    public AutomationDispatcher Dispatcher { get; }

    public UiaAutomation Automation { get; }

    public WindowQuery Windows { get; }

    public UiTreeService Tree { get; }

    public void Dispose()
    {
        Dispatcher.Dispose();
        if (AuditDirectory.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) && Directory.Exists(AuditDirectory))
            Directory.Delete(AuditDirectory, recursive: true);
    }
}
