using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Poly.Mcp.Surface;

namespace Poly.Mcp;

/// <summary>
/// One boundary for every tool call: an uncaught exception becomes a failed call
/// that still carries the advertised <see cref="PolyResponse"/> envelope (what the tools'
/// outputSchema promises), so agents see the same shape as a tool-level failure instead
/// of a bare protocol error.
/// </summary>
internal static class ToolExceptionBoundary {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

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
        var envelope = new PolyResponse(
            Ok: false,
            Summary: $"Internal error ({ex.GetType().Name}): {ex.Message}",
            Findings: [],
            Data: null,
            Next: [],
            State: null,
            ErrorCode: "INTERNAL_ERROR");
        return PolyEnvelopeText.Result(JsonSerializer.SerializeToElement(envelope, Json), isError: true);
    }
}