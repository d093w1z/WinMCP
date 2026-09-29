namespace WinMcp.Core.Errors;

/// <summary>Who is responsible for an error — the main tool for "agent mistake or WinMCP bug?" triage.</summary>
public enum ErrorCategory
{
    /// <summary>The caller (usually the agent) passed something wrong; retry with different arguments.</summary>
    Caller,

    /// <summary>WinMCP's security policy refused the request.</summary>
    Policy,

    /// <summary>The target application or desktop is in a state that prevents the operation.</summary>
    Environment,

    /// <summary>A WinMCP bug.</summary>
    Internal,
}
