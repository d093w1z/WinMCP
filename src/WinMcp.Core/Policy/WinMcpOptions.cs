namespace WinMcp.Core.Policy;

public enum ServerMode
{
    /// <summary>Read-only tools only. Default.</summary>
    Observe,

    /// <summary>Adds interaction tools. Requires a non-empty allowlist.</summary>
    Control,
}

/// <param name="Allow">Process names (<c>WinMcp.TestApp</c>, <c>.exe</c> optional) or full executable paths.</param>
public sealed record WinMcpOptions(ServerMode Mode, IReadOnlyList<string> Allow)
{
    public const string Usage =
        "Usage: WinMcp.Server [--mode observe|control] [--allow <process-name-or-exe-path>]...";

    /// <exception cref="ArgumentException">Invalid or inconsistent arguments; the message is user-facing.</exception>
    public static WinMcpOptions Parse(IReadOnlyList<string> args)
    {
        var mode = ServerMode.Observe;
        var allow = new List<string>();

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
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        if (mode == ServerMode.Control && allow.Count == 0)
            throw new ArgumentException("--mode control requires at least one --allow entry.");

        return new WinMcpOptions(mode, allow);
    }

    private static string Value(IReadOnlyList<string> args, ref int i)
    {
        var name = args[i];
        if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{name} requires a value.");
        return args[++i].Trim();
    }
}
