using System.Text.Json;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Poly.Mcp.Surface;

/// <summary>
/// The v2 tools reject unknown arguments by name instead of silently ignoring them. The
/// MCP binder only sees declared parameters, so the check runs in the request filter where
/// the raw argument bag is still visible. v1 tools are untouched.
/// </summary>
internal static class UnknownArgumentFilter {
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal) {
        ["poly_guide"] = ["topic"],
        ["poly_workspace"] = ["op", "name", "workspace"],
        ["poly_define"] = ["workspace", "poly", "apply", "edit"],
        ["poly_check"] = ["workspace", "select", "args", "expression", "bindings"],
        ["poly_inspect"] = ["workspace", "select", "detail"],
        ["poly_export"] = ["workspace", "format", "inline"],
        ["poly_simulate"] = ["workspace", "steps", "reset", "stopOnError"],
    };

    /// <summary>The accepted argument names per tool, for the reflection parity test.</summary>
    internal static IReadOnlyDictionary<string, string[]> AllowedArguments => Allowed;

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) {
        return async ValueTask<CallToolResult> (context, cancellationToken) => {
            var name = context.Params?.Name;
            if (name is not null
                && Allowed.TryGetValue(name, out var allowed)
                && context.Params?.Arguments is { } arguments) {
                var unknown = arguments.Keys
                    .Where(key => !allowed.Contains(key, StringComparer.Ordinal))
                    .OrderBy(key => key, StringComparer.Ordinal)
                    .ToList();
                if (unknown.Count > 0) {
                    var message = $"Unknown argument(s) for {name}: {string.Join(", ", unknown)}. " +
                        $"Valid: {string.Join(", ", allowed.OrderBy(a => a, StringComparer.Ordinal))}.";
                    return Error(name, message, "UNKNOWN_ARGUMENT");
                }
            }

            return await next(context, cancellationToken).ConfigureAwait(false);
        };
    }

    private static CallToolResult Error(string tool, string message, string errorCode) {
        var envelope = new {
            ok = false,
            summary = message,
            findings = Array.Empty<object>(),
            data = (object?)null,
            next = new[] { tool },
            state = (object?)null,
            errorCode,
        };
        var json = JsonSerializer.SerializeToElement(envelope);
        return new CallToolResult {
            IsError = true,
            StructuredContent = json,
            Content = [new TextContentBlock { Text = json.GetRawText() }],
        };
    }
}