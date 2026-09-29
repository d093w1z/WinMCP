namespace WinMcp.Core.Errors;

/// <summary>Stable error codes. Serialized as SCREAMING_SNAKE_CASE (see <see cref="WinMcpErrorCodeExtensions.ToWireName"/>).</summary>
public enum WinMcpErrorCode
{
    // Caller
    InvalidArgument,
    WindowNotFound,
    ElementNotFound,
    AmbiguousMatch,
    OptionNotFound,
    PatternNotSupported,
    ElementDisabled,
    ElementStale,

    // Policy
    TargetNotAllowed,
    OperationNotPermitted,
    PasswordField,

    // Environment
    AccessDeniedElevated,
    TargetNotResponding,
    WindowMinimized,
    WindowClosed,
    FocusFailed,
    Timeout,

    // Internal
    InternalError,
}

public static class WinMcpErrorCodeExtensions
{
    public static ErrorCategory Category(this WinMcpErrorCode code) => code switch
    {
        WinMcpErrorCode.InvalidArgument or
        WinMcpErrorCode.WindowNotFound or
        WinMcpErrorCode.ElementNotFound or
        WinMcpErrorCode.AmbiguousMatch or
        WinMcpErrorCode.OptionNotFound or
        WinMcpErrorCode.PatternNotSupported or
        WinMcpErrorCode.ElementDisabled or
        WinMcpErrorCode.ElementStale => ErrorCategory.Caller,

        WinMcpErrorCode.TargetNotAllowed or
        WinMcpErrorCode.OperationNotPermitted or
        WinMcpErrorCode.PasswordField => ErrorCategory.Policy,

        WinMcpErrorCode.AccessDeniedElevated or
        WinMcpErrorCode.TargetNotResponding or
        WinMcpErrorCode.WindowMinimized or
        WinMcpErrorCode.WindowClosed or
        WinMcpErrorCode.FocusFailed or
        WinMcpErrorCode.Timeout => ErrorCategory.Environment,

        WinMcpErrorCode.InternalError => ErrorCategory.Internal,

        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unmapped error code"),
    };

    /// <summary>
    /// True when repeating the <em>same</em> call unchanged may succeed later (transient conditions).
    /// Caller errors are never retryable as-is: the arguments must change.
    /// </summary>
    public static bool IsRetryable(this WinMcpErrorCode code) => code is
        WinMcpErrorCode.TargetNotResponding or
        WinMcpErrorCode.FocusFailed or
        WinMcpErrorCode.Timeout;

    /// <summary>Wire form, e.g. <c>ElementNotFound</c> → <c>ELEMENT_NOT_FOUND</c>.</summary>
    public static string ToWireName(this WinMcpErrorCode code)
    {
        var name = code.ToString();
        var builder = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) builder.Append('_');
            builder.Append(char.ToUpperInvariant(name[i]));
        }
        return builder.ToString();
    }
}
