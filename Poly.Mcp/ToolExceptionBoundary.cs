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
/// of a bare protocol error. A binder failure (the SDK could not bind the argument bag) is a
/// caller error and maps to <c>INVALID_ARGUMENT</c>; every other exception is a defect and stays
/// <c>INTERNAL_ERROR</c>, loudly.
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
                return Handle(ex, context.Params?.Name, logger);
            }
        };
    }

    /// <summary>
    /// Logs and classifies one escaped exception. A binder failure is a caller error at
    /// <c>Warning</c> naming the tool; anything else is a defect at <c>Error</c>.
    /// </summary>
    internal static CallToolResult Handle(Exception ex, string? tool, ILogger? logger) {
        if (IsBinderFailure(ex))
            logger?.LogWarning(ex, "MCP tool {Tool} rejected its arguments after validation", tool);
        else
            logger?.LogError(ex, "MCP tool {Tool} failed", tool);
        return Failure(ex);
    }

    /// <summary>The failed-call result every uncaught tool exception becomes.</summary>
    internal static CallToolResult Failure(Exception ex) =>
        IsBinderFailure(ex)
            ? Envelope("INVALID_ARGUMENT", $"Invalid argument: {ex.Message}")
            : Envelope("INTERNAL_ERROR", $"Internal error ({ex.GetType().Name}): {ex.Message}");

    /// <summary>The two shapes the SDK binder throws when the argument bag cannot be bound.</summary>
    internal static bool IsBinderFailure(Exception ex) =>
        ex is JsonException || ex is ArgumentException { ParamName: "arguments" };

    private static CallToolResult Envelope(string errorCode, string summary) {
        var envelope = new PolyResponse(
            Ok: false,
            Summary: summary,
            Findings: [],
            Data: null,
            Next: [],
            State: null,
            ErrorCode: errorCode);
        return PolyEnvelopeText.Result(JsonSerializer.SerializeToElement(envelope, Json), isError: true);
    }
}