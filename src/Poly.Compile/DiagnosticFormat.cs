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

    /// <summary>
    /// The diagnostics as a JSON array of records. Never filtered by verbosity. Written paths
    /// are not in this document; the batch command prints them on stderr.
    /// </summary>
    public static string Report(
        IReadOnlyList<CompileResult> units,
        IReadOnlyList<CompileDiagnostic> extra) {
        var all = units.SelectMany(u => u.Diagnostics).Concat(extra);
        return ToJson(all).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}