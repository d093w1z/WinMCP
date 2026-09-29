namespace WinMcp.Core.Automation;

/// <summary>A semantic action on one element. Implementations prefer UIA patterns and fall back to Win32 messages.</summary>
public abstract record ElementAction(string Name)
{
    public sealed record Invoke() : ElementAction("invoke");

    public sealed record SetValue(string Value) : ElementAction("set_value");

    /// <summary>Select the child item named <paramref name="Option"/> of a combo box, list or tab control.</summary>
    public sealed record Select(string Option) : ElementAction("select_option");

    public sealed record SetToggle(bool On) : ElementAction("set_toggle");
}

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

    public static bool ParseToggle(string? state) => state?.Trim().ToLowerInvariant() switch
    {
        "on" or "true" or "checked" => true,
        "off" or "false" or "unchecked" => false,
        _ => throw Invalid($"Unknown state '{state}'. Use 'on' or 'off'."),
    };

    private static Errors.WinMcpException Invalid(string message) =>
        new(new Errors.WinMcpError(Errors.WinMcpErrorCode.InvalidArgument, message));
}
