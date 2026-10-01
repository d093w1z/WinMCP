using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinMcp.Core.Errors;

namespace WinMcp.Server;

/// <summary>
/// Turns exceptions from tools into tool results with <c>isError: true</c> and a structured WinMCP error, so the
/// model can self-correct. Unexpected exceptions become INTERNAL_ERROR — by definition a WinMCP bug — and are logged.
/// </summary>
internal static class ToolErrorFilter
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Create(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            try
            {
                return await next(context, cancellationToken);
            }
            catch (WinMcpException ex)
            {
                return ToResult(ex.Error);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or McpException))
            {
                context.Services?.GetService<ILoggerFactory>()?.CreateLogger(typeof(ToolErrorFilter))
                    .LogError(ex, "Unhandled exception in tool {Tool}", context.Params?.Name);
                return ToResult(new WinMcpError(
                    WinMcpErrorCode.InternalError,
                    "WinMCP hit an internal error; details are in the server log.",
                    Hint: "This is a WinMCP bug, not a problem with the request."));
            }
        };

    public static CallToolResult ToResult(WinMcpError error)
    {
        var body = new ErrorBody(new ErrorDetail(
            error.Code.ToWireName(), error.Message, error.Category, error.Retryable, error.Hint, error.Details));
        var element = JsonSerializer.SerializeToElement(body, WinMcpJson.Options);
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = element.GetRawText() }],
            StructuredContent = element,
        };
    }

    private sealed record ErrorBody(ErrorDetail Error);

    private sealed record ErrorDetail(
        string Code,
        string Message,
        ErrorCategory Category,
        bool Retryable,
        string? Hint,
        IReadOnlyDictionary<string, object?>? Details);
}
