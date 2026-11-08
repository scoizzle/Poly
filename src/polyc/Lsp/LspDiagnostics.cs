using System.Text.Json.Nodes;

using Poly.Compile;

namespace Polyc.Lsp;

/// <summary>
/// The LSP spelling of <see cref="CompileDiagnostic"/>. Lossless: <see cref="FromLsp"/> returns the
/// record <see cref="ToLsp"/> was given, so a client sees what <c>polyc check --format json</c> reports.
/// </summary>
public static class LspDiagnostics {
    public const string Source = "polyc";

    public static JsonObject ToLsp(CompileDiagnostic d) => new() {
        ["range"] = new JsonObject {
            ["start"] = new JsonObject { ["line"] = d.Line - 1, ["character"] = d.Column - 1 },
            ["end"] = new JsonObject { ["line"] = d.EndLine - 1, ["character"] = d.EndColumn - 1 },
        },
        ["severity"] = SeverityNumber(d.Severity),
        ["code"] = d.Code,
        ["source"] = Source,
        ["message"] = d.Message,
        ["data"] = new JsonObject { ["path"] = d.Path, ["placed"] = d.Placed },
    };

    public static CompileDiagnostic FromLsp(string file, JsonNode node) => new(
        file,
        node["range"]!["start"]!["line"]!.GetValue<int>() + 1,
        node["range"]!["start"]!["character"]!.GetValue<int>() + 1,
        node["range"]!["end"]!["line"]!.GetValue<int>() + 1,
        node["range"]!["end"]!["character"]!.GetValue<int>() + 1,
        SeverityName(node["severity"]!.GetValue<int>()),
        node["code"]!.GetValue<string>(),
        node["message"]!.GetValue<string>(),
        node["data"]!["path"]!.GetValue<string>(),
        node["data"]!["placed"]!.GetValue<bool>());

    private static int SeverityNumber(string severity) => severity switch {
        Severities.Error => 1,
        Severities.Warning => 2,
        Severities.Information => 3,
        _ => 4,
    };

    private static string SeverityName(int number) => number switch {
        1 => Severities.Error,
        2 => Severities.Warning,
        3 => Severities.Information,
        _ => Severities.Hint,
    };
}