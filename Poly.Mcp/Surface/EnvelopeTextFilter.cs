using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Poly.Mcp.Surface;

/// <summary>
/// Innermost call filter: a tool result that carries a structured envelope gets its text
/// block replaced by the fixed-prose projection instead of the duplicated JSON. IsError and
/// the structured envelope are unchanged; results without structured content pass through.
/// </summary>
internal static class EnvelopeTextFilter {
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) {
        return async ValueTask<CallToolResult> (context, cancellationToken) =>
            PolyEnvelopeText.Apply(await next(context, cancellationToken).ConfigureAwait(false));
    }
}