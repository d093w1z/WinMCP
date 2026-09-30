using WinMcp.Core.Errors;

namespace WinMcp.Core.Automation;

/// <summary>A key combination: zero or more modifiers plus one key, e.g. Ctrl+Shift+S.</summary>
/// <param name="Key">Canonical key name, e.g. <c>Enter</c>, <c>F5</c>, <c>A</c>.</param>
/// <param name="VirtualKey">Win32 virtual-key code for <paramref name="Key"/>.</param>
public sealed record KeyChord(bool Ctrl, bool Shift, bool Alt, string Key, ushort VirtualKey)
{
    public override string ToString() =>
        string.Concat(Ctrl ? "Ctrl+" : "", Shift ? "Shift+" : "", Alt ? "Alt+" : "", Key);
}

/// <summary>What <c>send_keys</c> sends: literal text (as Unicode characters) or a sequence of key chords.</summary>
public abstract record KeyInput
{
    public sealed record Text(string Value) : KeyInput;

    public sealed record Chords(IReadOnlyList<KeyChord> Sequence) : KeyInput;
}

/// <summary>Parses <c>send_keys</c> arguments and enforces what may never be sent.</summary>
public static class KeyInputParser
{
    public const int MaxTextLength = 10_000;
    public const int MaxChords = 50;

    private static readonly Dictionary<string, (string Name, ushort Vk)> Keys = BuildKeys();

    /// <summary>
    /// Chords that move focus to another window (so later input would reach the wrong application) or open system UI.
    /// Anything involving the Windows key is rejected before lookup, since it isn't a supported key name at all.
    /// </summary>
    private static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alt+Tab", "Shift+Alt+Tab", "Alt+Esc", "Shift+Alt+Esc", "Ctrl+Esc", "Ctrl+Shift+Esc", "Ctrl+Alt+Delete",
    };

    public static KeyInput Parse(string? text, string? keys)
    {
        if ((text is null) == (keys is null))
            throw Invalid("Give exactly one of 'text' (typed literally) or 'keys' (e.g. \"Ctrl+A, Backspace, Enter\").");
        if (text is not null)
        {
            if (text.Length is 0 or > MaxTextLength)
                throw Invalid($"'text' must be 1–{MaxTextLength} characters.");
            return new KeyInput.Text(text);
        }

        var parts = keys!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > MaxChords)
            throw Invalid($"'keys' must contain 1–{MaxChords} comma-separated key combinations.");
        return new KeyInput.Chords(parts.Select(ParseChord).ToList());
    }

    private static KeyChord ParseChord(string chord)
    {
        bool ctrl = false, shift = false, alt = false;
        string? key = null;
        foreach (var token in chord.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; break;
                case "shift": shift = true; break;
                case "alt": alt = true; break;
                case "win" or "windows" or "lwin" or "rwin" or "meta" or "super":
                    throw Refused(chord, "the Windows key opens system UI and moves focus away from the application");
                default:
                    if (key is not null)
                        throw Invalid($"'{chord}' has more than one non-modifier key.");
                    key = token;
                    break;
            }
        }
        if (key is null || !Keys.TryGetValue(key, out var resolved))
            throw Invalid($"Unknown key in '{chord}'. Use letters, digits, F1–F24, or: {string.Join(", ", Keys.Values.Select(k => k.Name).Where(n => n.Length > 1 && !n.StartsWith('F')).Distinct())}.");

        var result = new KeyChord(ctrl, shift, alt, resolved.Name, resolved.Vk);
        if (Blocked.Contains(result.ToString()))
            throw Refused(chord, "it switches to another window, so further input would reach the wrong application");
        return result;
    }

    private static Dictionary<string, (string, ushort)> BuildKeys()
    {
        var keys = new Dictionary<string, (string, ushort)>(StringComparer.OrdinalIgnoreCase);
        void Add(ushort vk, string name, params string[] aliases)
        {
            keys[name] = (name, vk);
            foreach (var alias in aliases) keys[alias] = (name, vk);
        }

        Add(0x08, "Backspace", "Back", "Bksp");
        Add(0x09, "Tab");
        Add(0x0D, "Enter", "Return");
        Add(0x1B, "Esc", "Escape");
        Add(0x20, "Space");
        Add(0x21, "PageUp", "PgUp");
        Add(0x22, "PageDown", "PgDn");
        Add(0x23, "End");
        Add(0x24, "Home");
        Add(0x25, "Left");
        Add(0x26, "Up");
        Add(0x27, "Right");
        Add(0x28, "Down");
        Add(0x2D, "Insert", "Ins");
        Add(0x2E, "Delete", "Del");
        Add(0x5D, "Menu", "Apps", "ContextMenu");
        for (var c = 'A'; c <= 'Z'; c++) Add(c, c.ToString());
        for (var d = '0'; d <= '9'; d++) Add(d, d.ToString());
        for (var f = 1; f <= 24; f++) Add((ushort)(0x70 + f - 1), $"F{f}");
        return keys;
    }

    private static WinMcpException Invalid(string message) => new(new WinMcpError(WinMcpErrorCode.InvalidArgument, message));

    private static WinMcpException Refused(string chord, string why) =>
        new(new WinMcpError(WinMcpErrorCode.OperationNotPermitted, $"'{chord}' is not allowed: {why}."));
}
