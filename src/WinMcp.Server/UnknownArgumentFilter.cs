using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinMcp.Core.Errors;

namespace WinMcp.Server;

/// <summary>
/// Rejects arguments a tool doesn't have. The SDK ignores them, so a mistaken criterion (an agent passed
/// <c>value_contains</c> to <c>find_elements</c>) silently widened the search instead of being corrected.
/// </summary>
internal static class UnknownArgumentFilter
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Create(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            if (context.Params is { Name: { } name, Arguments: { Count: > 0 } arguments }
                && context.Server.ServerOptions.ToolCollection?.TryGetPrimitive(name, out var tool) == true)
            {
                var known = tool.ProtocolTool.InputSchema.TryGetProperty("properties", out var properties)
                    ? properties.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
                    : [];
                var unknown = arguments.Keys.Where(k => !known.Contains(k)).Order(StringComparer.Ordinal).ToList();
                if (unknown.Count > 0)
                    return ToolErrorFilter.ToResult(new WinMcpError(
                        WinMcpErrorCode.InvalidArgument,
                        $"{name} has no parameter {string.Join(", ", unknown.Select(u => $"'{u}'"))}.",
                        Hint: $"Valid parameters: {string.Join(", ", known.Order(StringComparer.Ordinal))}."));
            }
            return await next(context, cancellationToken);
        };
}
