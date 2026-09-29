namespace WinMcp.Core.Errors;

/// <summary>Agent-facing error. Category and retryability are derived from <see cref="Code"/> so they can't disagree.</summary>
public sealed record WinMcpError(
    WinMcpErrorCode Code,
    string Message,
    string? Hint = null,
    IReadOnlyDictionary<string, object?>? Details = null)
{
    public ErrorCategory Category => Code.Category();

    public bool Retryable => Code.IsRetryable();
}

/// <summary>Thrown by Core/Windows services; the server maps it to an MCP tool result with <c>isError: true</c>.</summary>
public sealed class WinMcpException(WinMcpError error, Exception? inner = null)
    : Exception(error.Message, inner)
{
    public WinMcpError Error { get; } = error;
}
