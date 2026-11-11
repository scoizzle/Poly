using System.Globalization;
using System.Text;
using System.Text.Json;

using ModelContextProtocol.Protocol;

namespace Poly.Mcp.Surface;

/// <summary>
/// The text block of a v2 result: a fixed-prose projection of the structured envelope. It
/// carries the essentials (outcome, workspace handle, findings, next calls) and the data
/// flattened to <c>key: value</c> lines, so a client that reads only the text block loses
/// nothing it needs. The full envelope stays in <c>structuredContent</c>. This departs on
/// purpose from the MCP SHOULD of "serialized JSON in a text block": the duplicate JSON was
/// the largest share of the payload.
/// </summary>
internal static class PolyEnvelopeText {
    /// <summary>Matches the cap the tools apply to findings.</summary>
    internal const int FindingCap = 20;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The failed or successful call result for one envelope: structured plus its text.</summary>
    public static CallToolResult Result(JsonElement envelope, bool isError) => new() {
        IsError = isError,
        StructuredContent = envelope,
        Content = [new TextContentBlock { Text = ProjectText(envelope) }],
    };

    /// <summary>Serializes an envelope object (nulls omitted, like the tools) into a result.</summary>
    public static CallToolResult Result(object envelope, bool isError) =>
        Result(JsonSerializer.SerializeToElement(envelope, Json), isError);

    /// <summary>
    /// Replaces the content of a result that carries a structured envelope with the text
    /// projection. Results without structured content pass through untouched.
    /// </summary>
    public static CallToolResult Apply(CallToolResult result) {
        if (result.StructuredContent is not { } envelope || envelope.ValueKind != JsonValueKind.Object)
            return result;
        result.Content = [new TextContentBlock { Text = ProjectText(envelope) }];
        return result;
    }

    public static string ProjectText(JsonElement envelope) {
        var lines = new List<string>();
        if (envelope.ValueKind != JsonValueKind.Object)
            return envelope.ToString();

        var ok = Bool(envelope, "ok") ?? true;
        var summary = One(Str(envelope, "summary") ?? "");
        var code = Str(envelope, "errorCode");
        lines.Add(ok && code is null
            ? $"ok — {summary}"
            : code is null ? $"error — {summary}" : $"error: {code} — {summary}");

        if (Obj(envelope, "state") is { } state)
            lines.Add($"workspace: {Str(state, "workspace")} model={Str(state, "model")} " +
                $"rev={Scalar(state, "revision")} entities={Scalar(state, "entities")} instances={Scalar(state, "instances")}");

        var data = envelope.TryGetProperty("data", out var d) ? d : default;
        AppendFindings(lines, envelope, data);
        if (data.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            AppendData(lines, data);

        if (envelope.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.Array) {
            var calls = next.EnumerateArray().Select(n => n.ToString()).Where(n => n.Length > 0).ToList();
            if (calls.Count > 0)
                lines.Add($"next: {string.Join(", ", calls)}");
        }

        return string.Join('\n', lines);
    }

    private static void AppendFindings(List<string> lines, JsonElement envelope, JsonElement data) {
        if (!envelope.TryGetProperty("findings", out var findings) || findings.ValueKind != JsonValueKind.Array)
            return;
        var all = findings.EnumerateArray().ToList();
        foreach (var finding in all.Take(FindingCap)) {
            var line = new StringBuilder("finding:");
            Part(line, Str(finding, "severity"));
            Part(line, Str(finding, "code"));
            Part(line, Str(finding, "path"));
            if (Scalar(finding, "line") is { } l)
                line.Append(" [").Append(l).Append(Scalar(finding, "column") is { } c ? $":{c}" : "").Append(']');
            Part(line, One(Str(finding, "message") ?? ""));
            if (Str(finding, "hint") is { Length: > 0 } hint)
                line.Append(" hint: ").Append(One(hint));
            if (Str(finding, "fix") is { Length: > 0 } fix)
                line.Append(" fix: ").Append(One(fix));
            lines.Add(line.ToString());
        }

        // A tool may have capped before the envelope was built; findingsTotal is the true count.
        var total = all.Count;
        if (data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("findingsTotal", out var t) && t.TryGetInt32(out var reported))
            total = Math.Max(total, reported);
        var shown = Math.Min(all.Count, FindingCap);
        if (total > shown)
            lines.Add($"+{total - shown} more");
    }

    private static void AppendData(List<string> lines, JsonElement data) {
        if (data.ValueKind == JsonValueKind.Object) {
            if (data.TryGetProperty("guide", out var guide) && guide.ValueKind == JsonValueKind.String) {
                lines.Add(guide.GetString()!);
                return;
            }

            if (data.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.String) {
                var bytes = Scalar(data, "bytes");
                if (Str(data, "content") is { } content) {
                    lines.Add($"artifact {format.GetString()} {bytes}B");
                    lines.Add(content);
                    return;
                }
                if (Str(data, "path") is { } path) {
                    lines.Add($"artifact {format.GetString()} {path} {bytes}B sha256={Str(data, "sha256")}");
                    return;
                }
            }
        }

        var flat = new List<(string Key, string Value)>();
        foreach (var property in data.ValueKind == JsonValueKind.Object ? data.EnumerateObject() : []) {
            if (property.NameEquals("witness") && property.Value.ValueKind == JsonValueKind.Array) {
                lines.Add($"witness: {property.Value.GetArrayLength()} context(s)");
                continue;
            }
            if (property.NameEquals("spliced")) {
                if (property.Value.ValueKind == JsonValueKind.String)
                    lines.Add($"spliced:\n{property.Value.GetString()}");
                continue;
            }
            if (property.NameEquals("trace") && property.Value.ValueKind == JsonValueKind.Array) {
                AppendTrace(lines, property.Value);
                continue;
            }
            Flatten(property.Name, property.Value, flat);
        }
        if (data.ValueKind != JsonValueKind.Object)
            Flatten("data", data, flat);
        lines.AddRange(flat.Select(f => $"{f.Key}: {f.Value}"));
    }

    private static void AppendTrace(List<string> lines, JsonElement trace) {
        var position = 0;
        foreach (var step in trace.EnumerateArray()) {
            var line = new StringBuilder("step ")
                .Append(Scalar(step, "index") ?? position.ToString(CultureInfo.InvariantCulture))
                .Append(' ').Append(Str(step, "op"))
                .Append(Bool(step, "ok") == false ? " FAILED: " : " ok: ")
                .Append(One(Str(step, "summary") ?? ""));
            if (step.TryGetProperty("data", out var stepData)) {
                var flat = new List<(string Key, string Value)>();
                Flatten("", stepData, flat);
                if (flat.Count > 0)
                    line.Append(" — ").Append(string.Join(", ", flat.Select(f => f.Key.Length == 0 ? f.Value : $"{f.Key}={f.Value}")));
            }
            lines.Add(line.ToString());
            position++;
        }
    }

    private static void Flatten(string key, JsonElement value, List<(string Key, string Value)> into) {
        switch (value.ValueKind) {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                    Flatten(key.Length == 0 ? property.Name : $"{key}.{property.Name}", property.Value, into);
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in value.EnumerateArray())
                    Flatten($"{key}[{index++}]", item, into);
                break;
            case JsonValueKind.Null or JsonValueKind.Undefined:
                break;
            case JsonValueKind.String:
                into.Add((key, One(value.GetString()!)));
                break;
            default:
                into.Add((key, value.GetRawText()));
                break;
        }
    }

    private static void Part(StringBuilder line, string? part) {
        if (!string.IsNullOrEmpty(part))
            line.Append(' ').Append(part);
    }

    private static string One(string text) => text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static bool? Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
        && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : null;

    private static string? Scalar(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
        && v.ValueKind is JsonValueKind.Number or JsonValueKind.String or JsonValueKind.True or JsonValueKind.False
            ? v.ToString() : null;

    private static JsonElement? Obj(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;
}