namespace WinMcp.Core.Policy;

public enum ServerMode
{
    /// <summary>Read-only tools only. Default.</summary>
    Observe,

    /// <summary>Adds interaction tools. Requires a non-empty allowlist.</summary>
    Control,
}

/// <param name="Allow">Process names (<c>WinMcp.TestApp</c>, <c>.exe</c> optional) or full executable paths.</param>
/// <param name="Symbols">Process name (no <c>.exe</c>) → path of that application's <c>resource.h</c>.</param>
public sealed record WinMcpOptions(ServerMode Mode, IReadOnlyList<string> Allow, IReadOnlyDictionary<string, string> Symbols)
{
    public const string Usage =
        "Usage: WinMcp.Server [--mode observe|control] [--allow <process-name-or-exe-path>]... [--symbols <process-name>=<path-to-resource.h>]... [--audit-dir <directory>]";

    /// <summary>Where control actions are logged (JSONL, one file per day).</summary>
    public string AuditDirectory { get; init; } = Audit.JsonlAuditLog.DefaultDirectory;

    public WinMcpOptions(ServerMode mode, IReadOnlyList<string> allow)
        : this(mode, allow, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
    {
    }

    /// <exception cref="ArgumentException">Invalid or inconsistent arguments; the message is user-facing.</exception>
    public static WinMcpOptions Parse(IReadOnlyList<string> args)
    {
        var mode = ServerMode.Observe;
        var allow = new List<string>();
        var symbols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? auditDirectory = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--mode":
                    // Explicit names only: Enum.TryParse would also accept "1" and silently enable control mode.
                    var modeText = Value(args, ref i);
                    mode = modeText.ToLowerInvariant() switch
                    {
                        "observe" => ServerMode.Observe,
                        "control" => ServerMode.Control,
                        _ => throw new ArgumentException($"Invalid --mode '{modeText}'. Expected 'observe' or 'control'."),
                    };
                    break;
                case "--allow":
                    allow.Add(Value(args, ref i));
                    break;
                case "--symbols":
                    var entry = Value(args, ref i);
                    var separator = entry.IndexOf('=');
                    if (separator <= 0 || separator == entry.Length - 1)
                        throw new ArgumentException($"Invalid --symbols '{entry}'. Expected <process-name>=<path-to-resource.h>.");
                    var process = entry[..separator].Trim();
                    if (process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        process = process[..^4];
                    var path = entry[(separator + 1)..].Trim().Trim('"');
                    if (!File.Exists(path))
                        throw new ArgumentException($"--symbols file not found: '{path}'.");
                    symbols[process] = Path.GetFullPath(path);
                    break;
                case "--audit-dir":
                    auditDirectory = Path.GetFullPath(Value(args, ref i).Trim('"'));
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        if (mode == ServerMode.Control && allow.Count == 0)
            throw new ArgumentException("--mode control requires at least one --allow entry.");

        var options = new WinMcpOptions(mode, allow, symbols);
        return auditDirectory is null ? options : options with { AuditDirectory = auditDirectory };
    }

    private static string Value(IReadOnlyList<string> args, ref int i)
    {
        var name = args[i];
        if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{name} requires a value.");
        return args[++i].Trim();
    }
}
