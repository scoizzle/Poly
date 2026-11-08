using System.Text.Json;
using System.Text.Json.Nodes;

namespace Poly.Compile;

/// <summary>Text and JSON renderings of diagnostics. Both name the same codes.</summary>
public static class DiagnosticFormat {
    /// <summary><c>Orders.poly:12:5: error DMSTR001: message</c></summary>
    public static string Text(CompileDiagnostic d) =>
        $"{d.File}:{d.Line}:{d.Column}: {d.Severity} {d.Code}: {d.Message}";

    /// <summary>
    /// Text lines for <paramref name="diagnostics"/> at or above <paramref name="verbosity"/>
    /// (<c>error</c> shows errors only is not offered: the default shows errors and warnings).
    /// </summary>
    public static IEnumerable<string> TextLines(IEnumerable<CompileDiagnostic> diagnostics, string verbosity = "warning") {
        var limit = Severities.Rank(verbosity);
        return diagnostics.Where(d => Severities.Rank(d.Severity) <= limit).Select(Text);
    }

    public static JsonObject ToJson(CompileDiagnostic d) => new() {
        ["file"] = d.File,
        ["line"] = d.Line,
        ["column"] = d.Column,
        ["endLine"] = d.EndLine,
        ["endColumn"] = d.EndColumn,
        ["severity"] = d.Severity,
        ["code"] = d.Code,
        ["message"] = d.Message,
        ["path"] = d.Path,
        ["placed"] = d.Placed,
    };

    public static JsonArray ToJson(IEnumerable<CompileDiagnostic> diagnostics) =>
        new([.. diagnostics.Select(d => (JsonNode?)ToJson(d))]);

    public static CompileDiagnostic FromJson(JsonNode node) => new(
        node["file"]!.GetValue<string>(),
        node["line"]!.GetValue<int>(),
        node["column"]!.GetValue<int>(),
        node["endLine"]!.GetValue<int>(),
        node["endColumn"]!.GetValue<int>(),
        node["severity"]!.GetValue<string>(),
        node["code"]!.GetValue<string>(),
        node["message"]!.GetValue<string>(),
        node["path"]!.GetValue<string>(),
        node["placed"]!.GetValue<bool>());

    /// <summary>The whole run as one JSON document. Never filtered by verbosity.</summary>
    public static string Report(
        string command,
        IReadOnlyList<CompileResult> units,
        IReadOnlyList<CompileDiagnostic> extra,
        IReadOnlyList<string>? written = null) {
        var all = units.SelectMany(u => u.Diagnostics).Concat(extra).ToList();
        var root = new JsonObject {
            ["tool"] = "polyc",
            ["command"] = command,
            ["succeeded"] = !all.Any(d => d.IsError),
            ["errors"] = all.Count(d => d.IsError),
            ["warnings"] = all.Count(d => d.IsWarning),
            ["units"] = new JsonArray([.. units.Select(u => (JsonNode?)new JsonObject {
                ["file"] = u.File,
                ["domain"] = u.DomainName,
                ["succeeded"] = u.Succeeded,
            })]),
            ["diagnostics"] = ToJson(all),
        };
        if (written is not null)
            root["written"] = new JsonArray([.. written.Select(w => (JsonNode?)JsonValue.Create(w))]);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}