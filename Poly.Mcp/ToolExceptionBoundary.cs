using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Poly.Mcp.Tools;

namespace Poly.Mcp;

/// <summary>
/// One boundary for every tool call: an uncaught exception becomes a failed call
/// that still carries the <see cref="DomainToolResponse"/> envelope, so agents see
/// the same shape as a tool-level failure instead of a bare protocol error.
/// </summary>
internal static class ToolExceptionBoundary {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) {
        return async ValueTask<CallToolResult> (context, cancellationToken) => {
            try {
                return await next(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) {
                var logger = context.Services?.GetService<ILoggerFactory>()?.CreateLogger("Poly.Mcp.ToolBoundary");
                logger?.LogError(ex, "MCP tool {Tool} failed", context.Params?.Name);
                return Failure(ex);
            }
        };
    }

    /// <summary>The failed-call result every uncaught tool exception becomes.</summary>
    internal static CallToolResult Failure(Exception ex) {
        var envelope = new DomainToolResponse(
            Success: false,
            Message: $"Internal error: {ex.Message}",
            Diagnostics: [ex.GetType().Name]);
        var json = JsonSerializer.SerializeToElement(envelope, Json);
        return new CallToolResult {
            IsError = true,
            StructuredContent = json,
            Content = [new TextContentBlock { Text = json.GetRawText() }],
        };
    }
}