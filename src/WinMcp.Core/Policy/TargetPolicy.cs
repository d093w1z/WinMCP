using WinMcp.Core.Desktop;

namespace WinMcp.Core.Policy;

/// <summary>Decides which processes WinMCP may expose or touch. Deny always wins over allow.</summary>
public sealed class TargetPolicy
{
    /// <summary>Security-sensitive system UI that is never exposed, whatever the configuration.</summary>
    public static readonly IReadOnlySet<string> DeniedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "consent",            // UAC prompt
        "LogonUI",
        "winlogon",
        "CredentialUIBroker", // Windows credential prompts
    };

    private readonly HashSet<string> _allowedNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _allowedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _ownPid;

    public TargetPolicy(WinMcpOptions options, int ownPid)
    {
        _ownPid = ownPid;
        foreach (var entry in options.Allow)
        {
            if (entry.Contains('\\') || entry.Contains('/'))
                _allowedPaths.Add(System.IO.Path.GetFullPath(entry));
            else
                _allowedNames.Add(entry.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? entry[..^4] : entry);
        }
        AllowList = options.Allow;
    }

    public IReadOnlyList<string> AllowList { get; }

    public bool IsAllowed(ProcessInfo process)
    {
        if (process.Pid == _ownPid || DeniedProcessNames.Contains(process.Name))
            return false;
        return _allowedNames.Contains(process.Name)
            || (process.Path is not null && _allowedPaths.Contains(process.Path));
    }
}
