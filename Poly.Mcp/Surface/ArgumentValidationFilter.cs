using System.Collections.Concurrent;
using System.Text.Json;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Poly.Mcp.Surface;

/// <summary>One tool argument as the published inputSchema declares it.</summary>
internal sealed record ArgumentSpec(string Name, IReadOnlySet<string> JsonTypes, bool Required);

/// <summary>
/// The argument contract of every tool, read once from the schema the server publishes in
/// <c>tools/list</c> (<see cref="McpServerTool.ProtocolTool"/> → <see cref="Tool.InputSchema"/>).
/// A schema that cannot be mapped throws, and <see cref="SelfCheck"/> runs that map for every
/// registered tool at startup, so the host never serves a tool whose caller errors would bypass
/// validation. One map per tool for the process.
/// </summary>
internal static class ArgumentSpecCatalog {
    private static readonly ConcurrentDictionary<string, IReadOnlyList<ArgumentSpec>> Cache = new(StringComparer.Ordinal);

    public static IReadOnlyList<ArgumentSpec> For(McpServerTool tool) =>
        Cache.GetOrAdd(tool.ProtocolTool.Name, _ => Map(tool.ProtocolTool.Name, tool.ProtocolTool.InputSchema));

    /// <summary>Maps every published tool's inputSchema once; throws on the first unmappable one.</summary>
    public static void SelfCheck(IEnumerable<McpServerTool> tools) {
        foreach (var tool in tools)
            For(tool);
    }

    /// <summary>Maps one published inputSchema; throws when a property or requirement is unreadable.</summary>
    public static IReadOnlyList<ArgumentSpec> Map(string tool, JsonElement schema) {
        if (schema.ValueKind != JsonValueKind.Object
            || !schema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Tool '{tool}' publishes no object inputSchema properties.");

        var required = schema.TryGetProperty("required", out var requiredNames) && requiredNames.ValueKind == JsonValueKind.Array
            ? requiredNames.EnumerateArray().Select(name => name.GetString()!).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        var specs = new List<ArgumentSpec>();
        foreach (var property in properties.EnumerateObject())
            specs.Add(new ArgumentSpec(property.Name, JsonTypes(tool, property.Name, property.Value), required.Contains(property.Name)));

        foreach (var name in required.Where(name => specs.All(spec => spec.Name != name)))
            throw new InvalidOperationException($"Tool '{tool}' requires '{name}', which it does not publish.");

        return specs;
    }

    private static IReadOnlySet<string> JsonTypes(string tool, string argument, JsonElement schema) {
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("type", out var type))
            throw new InvalidOperationException($"Tool '{tool}' argument '{argument}' publishes no JSON type.");

        var types = type.ValueKind switch {
            JsonValueKind.String => new[] { type.GetString()! },
            JsonValueKind.Array => type.EnumerateArray().Select(value => value.GetString()!).ToArray(),
            _ => throw new InvalidOperationException($"Tool '{tool}' argument '{argument}' publishes an unreadable type."),
        };
        if (types.Length == 0)
            throw new InvalidOperationException($"Tool '{tool}' argument '{argument}' publishes an empty type.");

        return new HashSet<string>(types, StringComparer.Ordinal);
    }
}

/// <summary>The typed caller error one validation pass found.</summary>
internal readonly record struct ValidationVerdict(string ErrorCode, string Message);

/// <summary>A verdict, or the instruction to hand a missing required workspace to the tool body.</summary>
internal readonly record struct ValidationOutcome(ValidationVerdict? Verdict, bool InsertEmptyWorkspace);

/// <summary>
/// The pre-binder argument check. It is pure: the spec and the limits are parameters, so a
/// synthetic map proves enforcement without a mutable static. Precedence, pinned by test:
/// unknown names, then an exceeded limit, then a missing required workspace (handed to the
/// tool body), then another missing required argument, then a wrong JSON type.
/// </summary>
internal static class ArgumentValidator {
    public static ValidationOutcome Validate(
        string tool,
        IDictionary<string, JsonElement>? arguments,
        IReadOnlyList<ArgumentSpec> spec,
        IReadOnlyDictionary<(string Tool, string Argument), ArgumentLimit> limits) {
        var names = spec.Select(argument => argument.Name).ToHashSet(StringComparer.Ordinal);

        if (arguments is not null) {
            var unknown = arguments.Keys
                .Where(key => !names.Contains(key))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();
            if (unknown.Count > 0) {
                var valid = string.Join(", ", spec.Select(argument => argument.Name).OrderBy(name => name, StringComparer.Ordinal));
                return Verdict("UNKNOWN_ARGUMENT",
                    $"Unknown argument(s) for {tool}: {string.Join(", ", unknown)}. Valid: {valid}.");
            }
        }

        if (arguments is not null) {
            foreach (var argument in spec) {
                if (arguments.TryGetValue(argument.Name, out var value)
                    && ArgumentLimits.Check(tool, argument.Name, value, limits) is { } limitMessage)
                    return Verdict("INVALID_ARGUMENT", limitMessage);
            }
        }

        // A required, absent workspace is a host status, not an argument error: hand it to the
        // tool body, whose TryWorkspace produces the one WORKSPACE_REQUIRED envelope. It is only
        // reported when every other argument is well-formed, so a defect here is never masked.
        var insertWorkspace = spec.Any(argument => argument.Name == "workspace" && argument.Required)
            && (arguments is null || !arguments.ContainsKey("workspace"));

        foreach (var argument in spec) {
            if (argument.Required && argument.Name != "workspace"
                && (arguments is null || !arguments.ContainsKey(argument.Name)))
                return Verdict("INVALID_ARGUMENT", $"Missing required argument '{argument.Name}' for {tool}.");
        }

        if (arguments is not null) {
            foreach (var argument in spec) {
                if (!arguments.TryGetValue(argument.Name, out var value))
                    continue;
                // TryWorkspace owns WORKSPACE_REQUIRED for an explicit null too (characterised on
                // master); a non-null wrong type is still INVALID_ARGUMENT.
                if (value.ValueKind == JsonValueKind.Null && argument.Name == "workspace" && argument.Required)
                    continue;
                if (!Accepts(argument.JsonTypes, value))
                    return Verdict("INVALID_ARGUMENT",
                        $"Argument '{argument.Name}' for {tool} must be a JSON {Expected(argument.JsonTypes)}, not {Describe(value)}.");
            }
        }

        return new ValidationOutcome(null, InsertEmptyWorkspace: insertWorkspace);
    }

    private static ValidationOutcome Verdict(string errorCode, string message) =>
        new(new ValidationVerdict(errorCode, message), InsertEmptyWorkspace: false);

    private static bool Accepts(IReadOnlySet<string> types, JsonElement value) => value.ValueKind switch {
        JsonValueKind.String => types.Contains("string"),
        JsonValueKind.True or JsonValueKind.False => types.Contains("boolean"),
        JsonValueKind.Number => types.Contains("number") || types.Contains("integer"),
        JsonValueKind.Object => types.Contains("object"),
        JsonValueKind.Array => types.Contains("array"),
        JsonValueKind.Null => types.Contains("null"),
        _ => false,
    };

    private static string Expected(IReadOnlySet<string> types) {
        var named = types.Where(type => type != "null").OrderBy(type => type, StringComparer.Ordinal).ToList();
        return named.Count == 0 ? "null" : string.Join(" or ", named);
    }

    private static string Describe(JsonElement value) => value.ValueKind switch {
        JsonValueKind.String => "a string",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Number => "a number",
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.Null => "null",
        _ => "an unsupported value",
    };
}

/// <summary>
/// The one argument-validation filter. It reads each tool's published inputSchema through
/// <see cref="ArgumentSpecCatalog"/> and runs <see cref="ArgumentValidator"/> before the SDK
/// binder, so a caller mistake becomes a typed envelope instead of INTERNAL_ERROR. It replaces
/// the hand-maintained allow-list in the same slot.
/// </summary>
internal static class ArgumentValidationFilter {
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) {
        return async ValueTask<CallToolResult> (context, cancellationToken) => {
            if (context.MatchedPrimitive is McpServerTool tool) {
                var name = tool.ProtocolTool.Name;
                var outcome = ArgumentValidator.Validate(name, context.Params?.Arguments, ArgumentSpecCatalog.For(tool), ArgumentLimits.Registry);
                if (outcome.Verdict is { } verdict)
                    return Error(name, verdict.Message, verdict.ErrorCode);
                if (outcome.InsertEmptyWorkspace && context.Params is { } parameters) {
                    parameters.Arguments ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    parameters.Arguments["workspace"] = JsonSerializer.SerializeToElement("");
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
        return PolyEnvelopeText.Result(JsonSerializer.SerializeToElement(envelope), isError: true);
    }
}