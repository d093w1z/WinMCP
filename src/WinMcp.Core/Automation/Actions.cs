namespace WinMcp.Core.Automation;

/// <summary>A semantic action on one element. Implementations prefer UIA patterns and fall back to Win32 messages.</summary>
public abstract record ElementAction(string Name)
{
    public sealed record Invoke() : ElementAction("invoke");

    public sealed record SetValue(string Value) : ElementAction("set_value");

    /// <summary>Select the child item named <paramref name="Option"/> of a combo box, list or tab control.</summary>
    public sealed record Select(string Option) : ElementAction("select_option");

    public sealed record SetToggle(bool On) : ElementAction("set_toggle");

    /// <summary>Expands or collapses a tree node, combo box, menu or other expandable element (target state).</summary>
    public sealed record SetExpanded(bool Expanded) : ElementAction("set_expanded");

    /// <summary>Gives the element keyboard focus (used by <c>send_keys</c> before typing).</summary>
    public sealed record Focus() : ElementAction("focus");
}

/// <summary>Sends keyboard input to a top-level window, which must be (and stay) the foreground window.</summary>
public interface IKeyboard
{
    /// <summary>
    /// Brings <paramref name="window"/> to the foreground, then sends <paramref name="input"/> in chunks, verifying
    /// before every chunk that the foreground still belongs to the window. Throws FOCUS_FAILED (nothing or only a
    /// prefix was sent) rather than ever typing into another window, and WINDOW_MINIMIZED for minimized windows.
    /// </summary>
    Task<KeyboardOutcome> SendAsync(Desktop.WindowHandle window, KeyInput input, CancellationToken cancellationToken);
}

/// <param name="Chunks">Units sent: characters for text, chords for keys.</param>
public sealed record KeyboardOutcome(string Method, int Chunks);

/// <summary>What an <see cref="IUiAutomation"/> implementation did.</summary>
/// <param name="Method">How it was done, e.g. <c>uia.InvokePattern</c> or <c>win32.BM_CLICK</c>; <c>none</c> when nothing had to change.</param>
/// <param name="Changed">False when the element was already in the requested state.</param>
/// <param name="ValueAfter">Value (or selected option) read back after the action, when applicable.</param>
/// <param name="StateAfter">Toggle state read back after the action (<c>on|off|indeterminate</c>), when applicable.</param>
/// <param name="Warning">The action happened but something is noteworthy, e.g. the app is still busy handling it.</param>
public sealed record ActionOutcome(string Method, bool Changed, string? ValueAfter = null, string? StateAfter = null, string? Warning = null);

/// <summary>Agent-facing result of an interaction tool.</summary>
public sealed record ActionResult(
    bool Ok,
    string Action,
    string Element,
    string Method,
    bool Changed,
    string? ValueAfter,
    string? StateAfter,
    string? Warning,
    long ElapsedMs);

/// <summary>Conditions for <c>wait_for</c>. "Text" is the element's value when it has one, otherwise its name.</summary>
public enum WaitCondition
{
    Exists,
    Gone,
    Enabled,
    TextEquals,
    TextContains,
}

public sealed record WaitResult(bool Ok, string Condition, long ElapsedMs, ElementMatch? Element);

/// <summary>Parses tool arguments (snake_case wire names) into actions and conditions.</summary>
public static class ActionArguments
{
    public static WaitCondition ParseCondition(string? condition) => condition?.Trim().ToLowerInvariant() switch
    {
        "exists" => WaitCondition.Exists,
        "gone" => WaitCondition.Gone,
        "enabled" => WaitCondition.Enabled,
        "text_equals" => WaitCondition.TextEquals,
        "text_contains" => WaitCondition.TextContains,
        _ => throw Invalid($"Unknown condition '{condition}'. Use exists, gone, enabled, text_equals or text_contains."),
    };

    public static bool ParseExpanded(string? state) => state?.Trim().ToLowerInvariant() switch
    {
        "expanded" or "expand" or "open" => true,
        "collapsed" or "collapse" or "close" or "closed" => false,
        _ => throw Invalid($"Unknown state '{state}'. Use 'expanded' or 'collapsed'."),
    };

    public static bool ParseToggle(string? state) => state?.Trim().ToLowerInvariant() switch
    {
        "on" or "true" or "checked" => true,
        "off" or "false" or "unchecked" => false,
        _ => throw Invalid($"Unknown state '{state}'. Use 'on' or 'off'."),
    };

    private static Errors.WinMcpException Invalid(string message) =>
        new(new Errors.WinMcpError(Errors.WinMcpErrorCode.InvalidArgument, message));
}
